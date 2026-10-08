using System.Diagnostics;
using System.Runtime.InteropServices;
using Dlt645.Core.Errors;
using Dlt645.Core.Protocol;
using Dlt645.Core.Transport;

namespace Dlt645.Core.Communication;

/// <summary>读数据的结果。</summary>
/// <param name="Data">数据域中 DI 之后的内容（已减 33H，多帧时已拼接）。</param>
public sealed record DataResult(
    uint Di,
    byte[] Data,
    MeterAddress ResponseAddress,
    TimeSpan Elapsed,
    int FrameCount,
    IReadOnlyList<MeterException> Warnings);

/// <summary>
/// DL/T 645-2007 主站（只读）。负责：组帧发送、接收拼帧、过滤红外回显、
/// 校验、重试、后续帧读取、应答地址/DI 核对。
/// 所有公开方法线程安全（内部串行化），在后台线程执行，不会阻塞界面。
/// </summary>
public sealed class MeterClient
{
    private readonly ISerialTransport _transport;
    private readonly object _gate = new();

    public MeterClient(ISerialTransport transport, CommOptions? options = null)
    {
        _transport = transport;
        Options = options ?? new CommOptions();
    }

    public CommOptions Options { get; }

    public ISerialTransport Transport => _transport;

    /// <summary>DI → 名称，用于报文说明。</summary>
    public Func<uint, string?>? DiNamer { get; set; }

    /// <summary>报文与过程信息（在后台线程触发）。</summary>
    public event Action<MonitorEntry>? FrameLogged;

    /// <summary>每次事务结束时触发（在后台线程触发）。</summary>
    public event Action<TransactionInfo>? TransactionCompleted;

    // ================================================================ 公共接口

    /// <summary>用公共地址读取通信地址（控制码 13H）。</summary>
    public Task<MeterAddress> ReadAddressAsync(CancellationToken ct = default) =>
        RunAsync("读取表号", () => ReadAddressCore(ct), ct);

    /// <summary>读数据（控制码 11H），自动处理后续帧。</summary>
    public Task<DataResult> ReadDataAsync(MeterAddress address, uint di, CancellationToken ct = default) =>
        RunAsync($"读数据 {DataId.Format(di)}", () => ReadDataCore(address, di, ct), ct);

    // ================================================================ 事务

    private Task<T> RunAsync<T>(string operation, Func<T> body, CancellationToken ct) =>
        Task.Run(() =>
        {
            lock (_gate)
            {
                var sw = Stopwatch.StartNew();
                try
                {
                    var result = body();
                    TransactionCompleted?.Invoke(new TransactionInfo(operation, true, ErrorCode.None, "成功", sw.Elapsed));
                    return result;
                }
                catch (MeterException ex)
                {
                    Log(MonitorKind.Error, null, ex.Message);
                    TransactionCompleted?.Invoke(new TransactionInfo(operation, false, ex.Code, ex.Message, sw.Elapsed));
                    throw;
                }
                catch (OperationCanceledException)
                {
                    Log(MonitorKind.Info, null, $"{operation}：已取消");
                    TransactionCompleted?.Invoke(new TransactionInfo(operation, false, ErrorCode.Cancelled, "已取消", sw.Elapsed));
                    throw;
                }
            }
        }, CancellationToken.None);

    private MeterAddress ReadAddressCore(CancellationToken ct)
    {
        var request = FrameCodec.BuildReadAddress(Options.SendPreamble);
        var frame = Exchange(request, f =>
        {
            if (f.Control == ControlCode.ReadAddressOk)
            {
                return f.Data.Length == 6
                    ? null
                    : new MeterException(ErrorCode.FrameFormatError, $"通信地址应答的数据长度应为 6 字节，实际 {f.Data.Length} 字节");
            }
            if (ControlCode.Function(f.Control) == ControlCode.ReadAddress && ControlCode.IsAbnormal(f.Control))
            {
                byte err = f.Data.Length > 0 ? f.Data[0] : (byte)0;
                throw new MeterException(ErrorCode.MeterErrorResponse, ErrorWord.Describe(err)) { MeterErrorWord = err };
            }
            return new MeterException(ErrorCode.UnexpectedResponse, $"期望控制码 93H，收到 {f.Control:X2}H");
        }, ct);
        return MeterAddress.FromFrameBytes(frame.Data);
    }

    private DataResult ReadDataCore(MeterAddress address, uint di, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var warnings = new List<MeterException>();
        var request = FrameCodec.BuildReadData(address, di, Options.SendPreamble);

        var frame = Exchange(request, f =>
        {
            if (f.Control == ControlCode.ReadDataError)
            {
                byte err = f.Data.Length > 0 ? f.Data[0] : (byte)0;
                throw new MeterException(ErrorCode.MeterErrorResponse, ErrorWord.Describe(err)) { MeterErrorWord = err };
            }
            if (f.Control is not (ControlCode.ReadDataOk or ControlCode.ReadDataOkMore))
                return new MeterException(ErrorCode.UnexpectedResponse, $"期望控制码 91H/B1H/D1H，收到 {f.Control:X2}H");
            if (f.Data.Length < 4)
                return new MeterException(ErrorCode.FrameFormatError, $"应答数据域长度 {f.Data.Length} 字节，不足 4 字节数据标识");
            uint respDi = DataId.FromWireBytes(f.Data);
            if (respDi != di)
                return new MeterException(ErrorCode.DiMismatch, $"请求 {DataId.Format(di)}，应答 {DataId.Format(respDi)}");
            return null;
        }, ct);

        var responseAddress = frame.MeterAddress;
        if (!address.Matches(responseAddress))
        {
            var w = new MeterException(ErrorCode.AddressMismatch, $"请求表号 {address}，应答表号 {responseAddress}");
            warnings.Add(w);
            Log(MonitorKind.Warning, null, w.Message);
        }

        var payload = new List<byte>(frame.Data.Skip(4));
        int frames = 1;
        bool more = ControlCode.HasFollow(frame.Control);
        byte seq = 1;
        while (more)
        {
            if (frames >= Options.MaxFollowFrames)
                throw new MeterException(ErrorCode.FollowFrameFailed, $"后续帧超过 {Options.MaxFollowFrames} 帧，已停止");

            ct.ThrowIfCancellationRequested();
            Log(MonitorKind.Info, null, $"应答有后续帧，读取第 {seq} 个后续帧");
            var followReq = FrameCodec.BuildReadFollow(address, di, seq, Options.SendPreamble);
            byte expectSeq = seq;
            Dlt645Frame follow;
            try
            {
                follow = Exchange(followReq, f =>
                {
                    if (f.Control == ControlCode.ReadFollowError)
                    {
                        byte err = f.Data.Length > 0 ? f.Data[0] : (byte)0;
                        throw new MeterException(ErrorCode.MeterErrorResponse, ErrorWord.Describe(err)) { MeterErrorWord = err };
                    }
                    if (f.Control is not (ControlCode.ReadFollowOk or ControlCode.ReadFollowOkMore))
                        return new MeterException(ErrorCode.UnexpectedResponse, $"期望控制码 92H/B2H/D2H，收到 {f.Control:X2}H");
                    if (f.Data.Length < 5)
                        return new MeterException(ErrorCode.FrameFormatError, "后续帧数据域长度不足");
                    uint respDi = DataId.FromWireBytes(f.Data);
                    if (respDi != di)
                        return new MeterException(ErrorCode.DiMismatch, $"后续帧 DI {DataId.Format(respDi)} 与请求 {DataId.Format(di)} 不一致");
                    if (f.Data[^1] != expectSeq)
                        return new MeterException(ErrorCode.UnexpectedResponse, $"后续帧序号应为 {expectSeq}，实际 {f.Data[^1]}");
                    return null;
                }, ct);
            }
            catch (MeterException ex) when (!ex.IsPortFault && ex.Code != ErrorCode.MeterErrorResponse)
            {
                throw new MeterException(ErrorCode.FollowFrameFailed, $"第 {seq} 个后续帧：{ex.Message}", ex);
            }

            payload.AddRange(follow.Data.Skip(4).Take(follow.Data.Length - 5));
            frames++;
            more = ControlCode.HasFollow(follow.Control);
            seq++;
        }

        return new DataResult(di, payload.ToArray(), responseAddress, sw.Elapsed, frames, warnings);
    }

    // ================================================================ 收发

    /// <summary>
    /// 发送请求并等待满足条件的应答，失败时按配置重试。
    /// <paramref name="check"/> 返回 null 表示接受；返回异常表示该帧不符合（继续等待）；
    /// 直接抛出异常表示不可重试的错误（如电表异常应答）。
    /// </summary>
    private Dlt645Frame Exchange(byte[] request, Func<Dlt645Frame, MeterException?> check, CancellationToken ct)
    {
        MeterException? last = null;
        int attempts = Math.Max(0, Options.Retries) + 1;
        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            if (attempt > 1)
                Log(MonitorKind.Info, null, $"第 {attempt - 1} 次重试（共 {attempts - 1} 次）");
            try
            {
                _transport.DiscardInput();
                Log(MonitorKind.Tx, request, DescribeBytes(request));
                _transport.Write(request);
                return Receive(check, ct);
            }
            catch (MeterException ex) when (ex.IsRetryable)
            {
                last = ex;
                Log(MonitorKind.Warning, null, ex.Message);
            }
        }
        throw last ?? new MeterException(ErrorCode.Timeout);
    }

    private Dlt645Frame Receive(Func<Dlt645Frame, MeterException?> check, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var buffer = new List<byte>(256);
        var chunk = new byte[256];
        MeterException? frameError = null;   // 损坏帧
        MeterException? skipError = null;    // 有效但不符合期望的帧
        bool echoSeen = false;
        long lastRxMs = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            long elapsed = sw.ElapsedMilliseconds;
            long remaining = Options.TimeoutMs - elapsed;
            if (remaining <= 0) break;
            if (frameError != null && elapsed - lastRxMs > Options.ErrorSettleMs) break;

            int n = _transport.Read(chunk, (int)Math.Min(remaining, 50));
            if (n <= 0) continue;
            lastRxMs = sw.ElapsedMilliseconds;
            for (int i = 0; i < n; i++) buffer.Add(chunk[i]);

            while (buffer.Count > 0)
            {
                var span = CollectionsMarshal.AsSpan(buffer);
                var r = FrameCodec.Scan(span);
                if (r.Status == FrameScanStatus.NeedMore)
                {
                    if (r.Consumed > 0) DropGarbage(buffer, r.Consumed);
                    break;
                }
                if (r.Status == FrameScanStatus.NoFrame)
                {
                    DropGarbage(buffer, buffer.Count);
                    break;
                }
                if (r.Status == FrameScanStatus.Invalid)
                {
                    var bad = span.Slice(r.Start, Math.Min(Math.Max(r.Length, 1), span.Length - r.Start)).ToArray();
                    Log(MonitorKind.Error, bad, r.Reason ?? "帧错误");
                    if (r.Error == FrameError.ChecksumMismatch)
                    {
                        frameError ??= new MeterException(ErrorCode.ChecksumError, r.Reason);
                    }
                    else if (r.Error == FrameError.EndMarkerMissing)
                    {
                        frameError ??= new MeterException(ErrorCode.FrameFormatError, r.Reason);
                    }
                    else
                    {
                        skipError ??= new MeterException(ErrorCode.FrameFormatError, r.Reason);
                    }
                    buffer.RemoveRange(0, r.Consumed);
                    continue;
                }

                // Found
                buffer.RemoveRange(0, r.Consumed);
                var frame = r.Frame!;
                if (!frame.IsResponse)
                {
                    // 红外半双工：收到的是自己发出的帧（D7=0 的主站帧），过滤掉
                    echoSeen = true;
                    Log(MonitorKind.Echo, frame.Raw, "红外回显（本机发出的帧），已过滤");
                    continue;
                }

                Log(MonitorKind.Rx, frame.Raw, FrameDescriber.Describe(frame, DiNamer));
                var verdict = check(frame);
                if (verdict is null) return frame;
                skipError ??= verdict;
                Log(MonitorKind.Warning, null, verdict.Message + "，已忽略该帧");
            }
        }

        if (frameError != null) throw frameError;
        if (skipError != null) throw skipError;
        if (buffer.Count > 0 && buffer.Any(b => b != FrameCodec.Preamble))
        {
            var partial = buffer.ToArray();
            Log(MonitorKind.Error, partial, "帧不完整");
            throw new MeterException(ErrorCode.IncompleteFrame, $"只收到 {partial.Length} 字节：{Hex.ToHex(partial)}");
        }
        throw new MeterException(ErrorCode.Timeout, echoSeen
            ? $"{Options.TimeoutMs}ms 内只收到红外头自身的回显，电表没有应答"
            : $"{Options.TimeoutMs}ms 内没有收到任何数据");
    }

    private void DropGarbage(List<byte> buffer, int count)
    {
        var dropped = buffer.GetRange(0, count);
        buffer.RemoveRange(0, count);
        if (dropped.Any(b => b != FrameCodec.Preamble))
            Log(MonitorKind.Warning, dropped.ToArray(), "丢弃无法识别的字节（干扰或残留数据）");
    }

    private string DescribeBytes(byte[] bytes)
    {
        var f = FrameCodec.TryDecode(bytes, out _);
        return f is null ? string.Empty : FrameDescriber.Describe(f, DiNamer);
    }

    private void Log(MonitorKind kind, byte[]? bytes, string text)
    {
        try { FrameLogged?.Invoke(new MonitorEntry(DateTime.Now, kind, bytes, text)); }
        catch { /* 日志异常不能影响通讯 */ }
    }
}
