using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.Tests;

public class DataFormatTests
{
    // 以下数据均为“接收顺序、已减 33H”的数据域（低字节在前）

    [Fact]
    public void Energy_XXXXXX_XX()
    {
        var o = DataFormat.Parse("XXXXXX.XX").Decode(Hex.Parse("56 34 12 00"));
        Assert.True(o.Valid);
        Assert.Equal("1234.56", o.Text);
        Assert.Equal(1234.56, o.Number!.Value, 6);
    }

    [Fact]
    public void Energy_SignBit_Negative()
    {
        // 组合有功 -12.34 kWh：最高字节最高位为符号位
        var o = DataFormat.Parse("XXXXXX.XX").Decode(Hex.Parse("34 12 00 80"), signed: true);
        Assert.Equal("-12.34", o.Text);
    }

    [Fact]
    public void Voltage_XXX_X()
    {
        Assert.Equal("220.1", DataFormat.Parse("XXX.X").Decode(Hex.Parse("01 22")).Text);
    }

    [Fact]
    public void Current_XXX_XXX_Signed()
    {
        var f = DataFormat.Parse("XXX.XXX");
        Assert.Equal(3, f.Length);
        Assert.Equal("5.123", f.Decode(Hex.Parse("23 51 00"), signed: true).Text);
        Assert.Equal("-5.123", f.Decode(Hex.Parse("23 51 80"), signed: true).Text);
    }

    [Fact]
    public void Power_XX_XXXX_And_PowerFactor()
    {
        Assert.Equal("1.2345", DataFormat.Parse("XX.XXXX").Decode(Hex.Parse("45 23 01")).Text);
        Assert.Equal("0.985", DataFormat.Parse("X.XXX").Decode(Hex.Parse("85 09"), signed: true).Text);
        Assert.Equal("-0.500", DataFormat.Parse("X.XXX").Decode(Hex.Parse("00 85"), signed: true).Text);
    }

    [Fact]
    public void Integer_NNNNNN()
    {
        var o = DataFormat.Parse("NNNNNN").Decode(Hex.Parse("00 16 00"));
        Assert.Equal("1600", o.Text);
    }

    [Fact]
    public void Address_KeepsLeadingZeros()
    {
        var f = DataFormat.Parse("NNNNNNNNNNNN");
        Assert.Equal(FormatKind.Digits, f.Kind);
        Assert.Equal("000012345678", f.Decode(Hex.Parse("78 56 34 12 00 00")).Text);
    }

    [Fact]
    public void DateTime_YYMMDDhhmm()
    {
        // 2024-05-20 13:45，低字节在前：45 13 20 05 24
        Assert.Equal("2024-05-20 13:45", DataFormat.Parse("YYMMDDhhmm").Decode(Hex.Parse("45 13 20 05 24")).Text);
    }

    [Fact]
    public void Date_YYMMDDWW()
    {
        // 2024-05-20 星期一：WW DD MM YY
        Assert.Equal("2024-05-20 星期一", DataFormat.Parse("YYMMDDWW").Decode(Hex.Parse("01 20 05 24")).Text);
    }

    [Fact]
    public void Time_hhmmss_And_SettlementDay()
    {
        Assert.Equal("08:30:05", DataFormat.Parse("hhmmss").Decode(Hex.Parse("05 30 08")).Text);
        Assert.Equal("01日 00时", DataFormat.Parse("DDhh").Decode(Hex.Parse("00 01")).Text);
    }

    [Fact]
    public void Ascii_IsReversedAndTrimmed()
    {
        // "220V" 按低字节在前传输为 00 00 'V' '0' '2' '2'
        var data = new byte[] { 0x00, 0x00, (byte)'V', (byte)'0', (byte)'2', (byte)'2' };
        Assert.Equal("220V", DataFormat.Parse("ASCII:6").Decode(data).Text);
    }

    [Fact]
    public void AllFF_IsInvalid()
    {
        var o = DataFormat.Parse("XXXXXX.XX").Decode(Hex.Parse("FF FF FF FF"));
        Assert.False(o.Valid);
    }

    [Fact]
    public void NonBcd_IsInvalid()
    {
        var o = DataFormat.Parse("XXX.X").Decode(Hex.Parse("1A 22"));
        Assert.False(o.Valid);
        Assert.Contains("非 BCD", o.Text);
    }

    [Fact]
    public void ShortData_IsInvalid()
    {
        var o = DataFormat.Parse("XXXXXX.XX").Decode(Hex.Parse("56 34"));
        Assert.False(o.Valid);
    }

    [Theory]
    [InlineData("XXXXXX.XX", 1234.56, false)]
    [InlineData("XXXXXX.XX", -1234.56, true)]
    [InlineData("XXX.XXX", -5.5, true)]
    [InlineData("XX.XX", 50.01, false)]
    public void EncodeDecode_RoundTrip(string spec, double value, bool signed)
    {
        var f = DataFormat.Parse(spec);
        var o = f.Decode(f.EncodeNumber(value, signed), signed);
        Assert.Equal(value, o.Number!.Value, 6);
    }

    [Fact]
    public void InvalidSpec_Rejected()
    {
        Assert.False(DataFormat.TryParse("XXX", out _, out _));   // 奇数位
        Assert.False(DataFormat.TryParse("ABC", out _, out _));
    }
}
