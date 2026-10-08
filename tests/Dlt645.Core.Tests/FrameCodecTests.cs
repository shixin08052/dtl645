using Dlt645.Core.Protocol;

namespace Dlt645.Core.Tests;

/// <summary>
/// 帧编解码测试。期望报文均为手工按规约计算（与程序实现无关），
/// 其中“读通信地址”“公共地址读正向有功总电能”为业内常见的标准示例报文。
/// </summary>
public class FrameCodecTests
{
    private static readonly MeterAddress Addr = MeterAddress.Parse("123456789012");

    [Fact]
    public void BuildReadAddress_MatchesStandardExample()
    {
        var bytes = FrameCodec.BuildReadAddress();
        Assert.Equal("FE FE FE FE 68 AA AA AA AA AA AA 68 13 00 DF 16", Hex.ToHex(bytes));
    }

    [Fact]
    public void BuildReadData_WildcardAddress_ForwardActiveTotal()
    {
        var bytes = FrameCodec.BuildReadData(MeterAddress.Wildcard, 0x00010000);
        Assert.Equal("FE FE FE FE 68 AA AA AA AA AA AA 68 11 04 33 33 34 33 AE 16", Hex.ToHex(bytes));
    }

    [Fact]
    public void BuildReadData_AddressIsLowByteFirst()
    {
        var bytes = FrameCodec.BuildReadData(Addr, 0x00010000, preamble: false);
        Assert.Equal("68 12 90 78 56 34 12 68 11 04 33 33 34 33 68 16", Hex.ToHex(bytes));
    }

    [Fact]
    public void BuildReadFollow_ContainsDiAndSequence()
    {
        var bytes = FrameCodec.BuildReadFollow(Addr, 0x03300001, 1, preamble: false);
        Assert.Equal("68 12 90 78 56 34 12 68 12 05 34 33 63 36 34 D1 16", Hex.ToHex(bytes));
    }

    [Fact]
    public void Scan_NormalResponse_DecodesDataMinus33()
    {
        var raw = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 54 16");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.Found, r.Status);
        Assert.Equal(raw.Length, r.Consumed);
        var f = r.Frame!;
        Assert.Equal(0x91, f.Control);
        Assert.Equal("123456789012", f.MeterAddress.Text);
        Assert.Equal(0x00010000u, DataId.FromWireBytes(f.Data));
        Assert.Equal("00 00 01 00 56 34 12 00", Hex.ToHex(f.Data));
    }

    [Fact]
    public void Scan_SkipsAnyNumberOfPreambleBytes()
    {
        var raw = Hex.Parse("FE FE FE FE FE FE FE 68 12 90 78 56 34 12 68 93 06 45 C3 AB 89 67 45 07 16");
        var f = FrameCodec.TryDecode(raw, out var error);
        Assert.NotNull(f);
        Assert.Null(error);
        Assert.Equal(0x93, f!.Control);
        Assert.Equal("123456789012", MeterAddress.FromFrameBytes(f.Data).Text);
    }

    [Fact]
    public void Scan_ChecksumError_ReportsReason()
    {
        var raw = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 55 16");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.Invalid, r.Status);
        Assert.Equal(FrameError.ChecksumMismatch, r.Error);
        Assert.Contains("54H", r.Reason);
        Assert.Contains("55H", r.Reason);
    }

    [Fact]
    public void Scan_MissingEndMarker_ReportsReason()
    {
        var raw = Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 54 17");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.Invalid, r.Status);
        Assert.Equal(FrameError.EndMarkerMissing, r.Error);
        Assert.Contains("16H", r.Reason);
    }

    [Fact]
    public void Scan_SecondStartMarkerWrong_ReportsHeaderError()
    {
        var raw = Hex.Parse("68 12 90 78 56 34 12 69 91 08 33 33 34 33 89 67 45 33 54 16");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.Invalid, r.Status);
        Assert.Equal(FrameError.HeaderMismatch, r.Error);
    }

    [Fact]
    public void Scan_LengthTooLong_ReportsLengthError()
    {
        var raw = Hex.Parse("68 12 90 78 56 34 12 68 91 FF 33 33");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.Invalid, r.Status);
        Assert.Equal(FrameError.LengthTooLong, r.Error);
    }

    [Fact]
    public void Scan_IncompleteFrame_NeedsMore()
    {
        var raw = Hex.Parse("FE FE 68 12 90 78 56 34 12 68 91 08 33 33");
        var r = FrameCodec.Scan(raw);
        Assert.Equal(FrameScanStatus.NeedMore, r.Status);
        Assert.Equal(2, r.Consumed); // 前导 FE 可以丢弃
    }

    [Fact]
    public void Scan_OnlyPreamble_NoFrame()
    {
        var r = FrameCodec.Scan(Hex.Parse("FE FE FE FE"));
        Assert.Equal(FrameScanStatus.NoFrame, r.Status);
        Assert.Equal(4, r.Consumed);
    }

    [Fact]
    public void Scan_EchoThenResponse_BothFramesFoundInOrder()
    {
        var echo = FrameCodec.BuildReadData(Addr, 0x00010000);
        var resp = Hex.Parse("FE FE 68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 54 16");
        var stream = echo.Concat(resp).ToArray();

        var r1 = FrameCodec.Scan(stream);
        Assert.Equal(FrameScanStatus.Found, r1.Status);
        Assert.False(r1.Frame!.IsResponse); // 回显：D7=0
        var r2 = FrameCodec.Scan(stream.AsSpan(r1.Consumed));
        Assert.Equal(FrameScanStatus.Found, r2.Status);
        Assert.True(r2.Frame!.IsResponse);
    }

    [Fact]
    public void ExceptionResponse_ErrorWordDecodedBitByBit()
    {
        var f = FrameCodec.TryDecode(Hex.Parse("68 12 90 78 56 34 12 68 D1 01 35 8D 16"), out _)!;
        Assert.Equal(ControlCode.ReadDataError, f.Control);
        Assert.Equal(0x02, f.Data[0]);
        var bits = ErrorWord.Decode(f.Data[0]);
        Assert.Single(bits);
        Assert.Contains("无请求数据", bits[0]);

        var all = ErrorWord.Decode(0x7F);
        Assert.Equal(7, all.Count);
        Assert.Contains(all, s => s.Contains("其他错误"));
        Assert.Contains(all, s => s.Contains("密码错或未授权"));
        Assert.Contains(all, s => s.Contains("通信速率不能更改"));
        Assert.Contains(all, s => s.Contains("年时区数超"));
        Assert.Contains(all, s => s.Contains("日时段数超"));
        Assert.Contains(all, s => s.Contains("费率数超"));
    }

    [Fact]
    public void ControlCodeBits()
    {
        Assert.True(ControlCode.IsResponse(0x91));
        Assert.False(ControlCode.IsResponse(0x11));
        Assert.True(ControlCode.HasFollow(0xB1));
        Assert.False(ControlCode.HasFollow(0x91));
        Assert.True(ControlCode.IsAbnormal(0xD1));
    }

    [Fact]
    public void Describe_ProducesChineseText()
    {
        var f = FrameCodec.TryDecode(Hex.Parse("68 12 90 78 56 34 12 68 91 08 33 33 34 33 89 67 45 33 54 16"), out _)!;
        var text = FrameDescriber.Describe(f, di => di == 0x00010000 ? "正向有功总电能" : null);
        Assert.Contains("正常应答", text);
        Assert.Contains("00010000", text);
        Assert.Contains("正向有功总电能", text);
    }
}
