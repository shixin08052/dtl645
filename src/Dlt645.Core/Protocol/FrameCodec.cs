namespace Dlt645.Core.Protocol;

public enum FrameScanStatus
{
    /// <summary>找到一条校验通过的完整帧。</summary>
    Found,
    /// <summary>已找到帧头，但数据还没收全。</summary>
    NeedMore,
    /// <summary>找到疑似帧但校验失败，<see cref="FrameScanResult.Reason"/> 给出原因。</summary>
    Invalid,
    /// <summary>缓冲区中没有帧起始符（全部是前导 FE 或杂散字节）。</summary>
    NoFrame,
}

public enum FrameError
{
    None,
    /// <summary>第二个 68H 不在规定位置。</summary>
    HeaderMismatch,
    /// <summary>长度域 L 超过规约上限。</summary>
    LengthTooLong,
    /// <summary>校验和 CS 不一致。</summary>
    ChecksumMismatch,
    /// <summary>结束符不是 16H。</summary>
    EndMarkerMissing,
}

/// <param name="Consumed">调用方应从缓冲区头部移除的字节数。</param>
/// <param name="Start">疑似帧在缓冲区中的起始位置（用于日志）。</param>
/// <param name="Length">疑似帧的长度（Invalid 时可能为 0）。</param>
public readonly record struct FrameScanResult(
    FrameScanStatus Status,
    Dlt645Frame? Frame,
    int Consumed,
    FrameError Error = FrameError.None,
    string? Reason = null,
    int Start = 0,
    int Length = 0);

/// <summary>DL/T 645-2007 帧编解码。</summary>
public static class FrameCodec
{
    public const byte Preamble = 0xFE;
    public const int PreambleCount = 4;
    public const byte StartMarker = 0x68;
    public const byte EndMarker = 0x16;
    public const byte DataOffset = 0x33;
    /// <summary>规约规定读数据时 L ≤ 200。</summary>
    public const int MaxDataLength = 200;
    /// <summary>不含数据域的帧长度：68 + 6 地址 + 68 + C + L + CS + 16。</summary>
    public const int FrameOverhead = 12;

    public static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (var b in bytes) sum += b;
        return (byte)sum;
    }

    /// <summary>组帧。<paramref name="plainData"/> 为未加 33H 的数据域。</summary>
    public static byte[] Build(ReadOnlySpan<byte> address, byte control, ReadOnlySpan<byte> plainData, bool preamble = true)
    {
        if (address.Length != 6) throw new ArgumentException("地址必须为 6 字节", nameof(address));
        if (plainData.Length > MaxDataLength) throw new ArgumentException("数据域过长", nameof(plainData));

        int pre = preamble ? PreambleCount : 0;
        var frame = new byte[pre + FrameOverhead + plainData.Length];
        int p = 0;
        for (; p < pre; p++) frame[p] = Preamble;
        int start = p;
        frame[p++] = StartMarker;
        address.CopyTo(frame.AsSpan(p));
        p += 6;
        frame[p++] = StartMarker;
        frame[p++] = control;
        frame[p++] = (byte)plainData.Length;
        foreach (var d in plainData) frame[p++] = (byte)(d + DataOffset);
        frame[p] = Checksum(frame.AsSpan(start, p - start));
        p++;
        frame[p] = EndMarker;
        return frame;
    }

    /// <summary>读通信地址：68 AA AA AA AA AA AA 68 13 00 DF 16。</summary>
    public static byte[] BuildReadAddress(bool preamble = true) =>
        Build(MeterAddress.Wildcard.ToFrameBytes(), ControlCode.ReadAddress, ReadOnlySpan<byte>.Empty, preamble);

    public static byte[] BuildReadData(MeterAddress address, uint di, bool preamble = true) =>
        Build(address.ToFrameBytes(), ControlCode.ReadData, DataId.ToWireBytes(di), preamble);

    /// <summary>读后续数据：数据域为 DI0..DI3 + 帧序号 SEQ。</summary>
    public static byte[] BuildReadFollow(MeterAddress address, uint di, byte seq, bool preamble = true)
    {
        var data = new byte[5];
        DataId.ToWireBytes(di).CopyTo(data, 0);
        data[4] = seq;
        return Build(address.ToFrameBytes(), ControlCode.ReadFollow, data, preamble);
    }

    /// <summary>
    /// 从接收缓冲区头部开始查找一帧。会跳过任意数量的前导 FE 及杂散字节。
    /// 调用方根据 <see cref="FrameScanResult.Consumed"/> 移除已处理的字节后可继续调用。
    /// </summary>
    public static FrameScanResult Scan(ReadOnlySpan<byte> buffer)
    {
        int i = buffer.IndexOf(StartMarker);
        if (i < 0)
        {
            return new FrameScanResult(FrameScanStatus.NoFrame, null, buffer.Length);
        }

        // 至少需要读到长度域 L（位于 68 后第 9 个字节）
        if (buffer.Length - i < 10)
        {
            return new FrameScanResult(FrameScanStatus.NeedMore, null, i, Start: i);
        }

        if (buffer[i + 7] != StartMarker)
        {
            return new FrameScanResult(FrameScanStatus.Invalid, null, i + 1, FrameError.HeaderMismatch,
                $"帧头错误：第 8 字节应为 68H，实际为 {buffer[i + 7]:X2}H", i, 8);
        }

        int len = buffer[i + 9];
        if (len > MaxDataLength)
        {
            return new FrameScanResult(FrameScanStatus.Invalid, null, i + 1, FrameError.LengthTooLong,
                $"长度错误：数据域长度 L={len} 超过规约上限 {MaxDataLength}", i, 10);
        }

        int total = FrameOverhead + len;
        if (buffer.Length - i < total)
        {
            return new FrameScanResult(FrameScanStatus.NeedMore, null, i, Start: i, Length: total);
        }

        var frameSpan = buffer.Slice(i, total);
        byte expected = Checksum(frameSpan[..(10 + len)]);
        byte actual = frameSpan[10 + len];
        if (expected != actual)
        {
            return new FrameScanResult(FrameScanStatus.Invalid, null, i + 1, FrameError.ChecksumMismatch,
                $"校验和错误：计算值 {expected:X2}H，帧内值 {actual:X2}H", i, total);
        }

        if (frameSpan[total - 1] != EndMarker)
        {
            return new FrameScanResult(FrameScanStatus.Invalid, null, i + 1, FrameError.EndMarkerMissing,
                $"帧尾错误：结束符应为 16H，实际为 {frameSpan[total - 1]:X2}H", i, total);
        }

        var address = frameSpan.Slice(1, 6).ToArray();
        byte control = frameSpan[8];
        var data = new byte[len];
        for (int k = 0; k < len; k++) data[k] = (byte)(frameSpan[10 + k] - DataOffset);

        var frame = new Dlt645Frame(address, control, data, frameSpan.ToArray());
        return new FrameScanResult(FrameScanStatus.Found, frame, i + total, Start: i, Length: total);
    }

    /// <summary>便捷方法：从完整字节流中解出第一条有效帧（跳过回显以外的所有检查交由调用方）。</summary>
    public static Dlt645Frame? TryDecode(ReadOnlySpan<byte> bytes, out string? error)
    {
        error = null;
        var buf = bytes;
        while (!buf.IsEmpty)
        {
            var r = Scan(buf);
            switch (r.Status)
            {
                case FrameScanStatus.Found:
                    return r.Frame;
                case FrameScanStatus.NeedMore:
                    error ??= "帧不完整：数据长度不足";
                    return null;
                case FrameScanStatus.NoFrame:
                    error ??= "未找到帧起始符 68H";
                    return null;
                default:
                    error ??= r.Reason;
                    buf = buf[r.Consumed..];
                    break;
            }
        }
        error ??= "未找到有效帧";
        return null;
    }
}
