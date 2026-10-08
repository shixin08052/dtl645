using Dlt645.Core.Protocol;

namespace Dlt645.Core.Tests;

public class AddressAndDiTests
{
    [Theory]
    [InlineData("123456789012", "123456789012")]
    [InlineData("1234", "000000001234")]
    [InlineData(" 12345678 ", "000012345678")]
    [InlineData("AAAAAAAAAAAA", "AAAAAAAAAAAA")]
    [InlineData("aaaa", "AAAAAAAAAAAA")]
    [InlineData("AAAA12345678", "AAAA12345678")]
    public void ValidAddresses(string input, string expected)
    {
        Assert.True(MeterAddress.TryParse(input, out var a, out var err), err);
        Assert.Equal(expected, a!.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("1234567890123")]
    [InlineData("12345678901X")]
    [InlineData("A12345678901")]
    public void InvalidAddresses(string input)
    {
        Assert.False(MeterAddress.TryParse(input, out _, out var err));
        Assert.False(string.IsNullOrEmpty(err));
    }

    [Fact]
    public void AddressFrameBytes_RoundTrip()
    {
        var a = MeterAddress.Parse("123456789012");
        Assert.Equal("12 90 78 56 34 12", Hex.ToHex(a.ToFrameBytes()));
        Assert.Equal(a, MeterAddress.FromFrameBytes(a.ToFrameBytes()));
    }

    [Fact]
    public void WildcardMatching()
    {
        var actual = MeterAddress.Parse("000012345678");
        Assert.True(MeterAddress.Wildcard.Matches(actual));
        Assert.True(MeterAddress.Parse("AAAA12345678").Matches(actual));
        Assert.True(MeterAddress.Parse("12345678").Matches(actual));
        Assert.False(MeterAddress.Parse("000012345679").Matches(actual));
    }

    [Theory]
    [InlineData("00010000", 0x00010000u)]
    [InlineData("00 01 00 00", 0x00010000u)]
    [InlineData("0x0201FF00", 0x0201FF00u)]
    [InlineData("04000401", 0x04000401u)]
    public void ParseDi(string input, uint expected)
    {
        Assert.True(DataId.TryParse(input, out var di, out _));
        Assert.Equal(expected, di);
    }

    [Theory]
    [InlineData("0001")]
    [InlineData("0001000G")]
    [InlineData("")]
    public void ParseDi_Invalid(string input)
    {
        Assert.False(DataId.TryParse(input, out _, out var err));
        Assert.NotNull(err);
    }

    [Fact]
    public void DiWireOrder_Di0First()
    {
        Assert.Equal("00 00 01 00", Hex.ToHex(DataId.ToWireBytes(0x00010000)));
        Assert.Equal("01 04 00 04", Hex.ToHex(DataId.ToWireBytes(0x04000401)));
    }
}
