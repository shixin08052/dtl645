using System.Diagnostics;
using Dlt645.Core.Errors;
using Dlt645.Core.Protocol;
using Dlt645.Core.Transport;

namespace Dlt645.Core.Simulation;

/// <summary>
/// 模拟串口：把写入的帧交给 <see cref="SimulatedMeter"/>，并按设定的延迟返回应答。
/// 默认模拟红外半双工回显（发出的帧会原样先“收到”一次）。
/// </summary>
public sealed class SimulatedTransport : ISerialTransport
{
    private readonly object _gate = new();
    private readonly Queue<(long DueMs, byte[] Data)> _queue = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private byte[] _partial = Array.Empty<byte>();
    private bool _open;

    public SimulatedTransport(SimulatedMeter meter) => Meter = meter;

    public SimulatedMeter Meter { get; }

    public string DisplayName => "模拟电表";

    public bool IsOpen => _open;

    /// <summary>模拟红外回显。</summary>
    public bool Echo { get; set; } = true;

    /// <summary>电表应答延迟。</summary>
    public int ResponseDelayMs { get; set; } = 80;

    /// <summary>为 false 时电表不应答（用于演示超时）。</summary>
    public bool Respond { get; set; } = true;

    /// <summary>接下来 N 次应答的校验和被故意破坏（用于测试重试）。</summary>
    public int CorruptNextResponses { get; set; }

    /// <summary>每次 Read 最多返回的字节数，模拟串口分段到达。</summary>
    public int ChunkSize { get; set; } = 7;

    public void Open()
    {
        lock (_gate)
        {
            _open = true;
            _queue.Clear();
            _partial = Array.Empty<byte>();
        }
    }

    public void Close()
    {
        lock (_gate)
        {
            _open = false;
            _queue.Clear();
            Monitor.PulseAll(_gate);
        }
    }

    public void DiscardInput()
    {
        lock (_gate)
        {
            EnsureOpen();
            _queue.Clear();
            _partial = Array.Empty<byte>();
        }
    }

    public void Write(byte[] data)
    {
        lock (_gate)
        {
            EnsureOpen();
            long now = _clock.ElapsedMilliseconds;
            if (Echo) _queue.Enqueue((now, (byte[])data.Clone()));

            var frame = FrameCodec.TryDecode(data, out _);
            if (frame != null && Respond)
            {
                byte[]? response;
                try
                {
                    response = Meter.Handle(frame);
                }
                catch
                {
                    // 配置表示例值有误等情况：模拟电表回“其他错误”，而不是让程序崩溃
                    response = FrameCodec.Build(Meter.Address.ToFrameBytes(), (byte)(frame.Control | 0xC0), new byte[] { 0x01 }, preamble: false);
                }
                if (response != null)
                {
                    if (CorruptNextResponses > 0)
                    {
                        CorruptNextResponses--;
                        response[^2] ^= 0x5A; // 破坏 CS
                    }
                    _queue.Enqueue((now + ResponseDelayMs, response));
                }
            }
            Monitor.PulseAll(_gate);
        }
    }

    public int Read(byte[] buffer, int timeoutMs)
    {
        long deadline = _clock.ElapsedMilliseconds + timeoutMs;
        lock (_gate)
        {
            while (true)
            {
                EnsureOpen();
                if (_partial.Length == 0 && _queue.Count > 0 && _queue.Peek().DueMs <= _clock.ElapsedMilliseconds)
                    _partial = _queue.Dequeue().Data;

                if (_partial.Length > 0)
                {
                    int n = Math.Min(Math.Min(ChunkSize, buffer.Length), _partial.Length);
                    Array.Copy(_partial, buffer, n);
                    _partial = _partial[n..];
                    return n;
                }

                long now = _clock.ElapsedMilliseconds;
                if (now >= deadline) return 0;
                long wait = deadline - now;
                if (_queue.Count > 0) wait = Math.Min(wait, Math.Max(1, _queue.Peek().DueMs - now));
                Monitor.Wait(_gate, (int)Math.Max(1, wait));
            }
        }
    }

    public void Dispose() => Close();

    private void EnsureOpen()
    {
        if (!_open) throw new MeterException(ErrorCode.NotConnected, "模拟电表未打开");
    }
}
