using System.Globalization;
using System.Text;

namespace Dlt645.Core.Protocol;

/// <summary>HEX 字符串与 BCD 码的辅助方法。</summary>
public static class Hex
{
    public static string ToHex(ReadOnlySpan<byte> data, string separator = " ")
    {
        if (data.IsEmpty) return string.Empty;
        var sb = new StringBuilder(data.Length * (2 + separator.Length));
        for (int i = 0; i < data.Length; i++)
        {
            if (i > 0) sb.Append(separator);
            sb.Append(data[i].ToString("X2", CultureInfo.InvariantCulture));
        }
        return sb.ToString();
    }

    /// <summary>按高字节在前的顺序输出（即把接收顺序倒过来）。</summary>
    public static string ToHexReversed(ReadOnlySpan<byte> data, string separator = "")
    {
        var copy = data.ToArray();
        Array.Reverse(copy);
        return ToHex(copy, separator);
    }

    /// <summary>解析 HEX 字符串，允许空格、短横线、逗号与 0x 前缀。</summary>
    public static bool TryParse(string? text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Replace("0x", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(" ", "").Replace("-", "").Replace(",", "")
                    .Replace("\t", "").Replace("\r", "").Replace("\n", "");
        if (s.Length == 0 || s.Length % 2 != 0) return false;
        var result = new byte[s.Length / 2];
        for (int i = 0; i < result.Length; i++)
        {
            if (!byte.TryParse(s.AsSpan(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result[i]))
                return false;
        }
        bytes = result;
        return true;
    }

    public static byte[] Parse(string text) =>
        TryParse(text, out var b) ? b : throw new FormatException($"无效的 HEX 字符串：{text}");
}

public static class Bcd
{
    public static bool IsValid(byte b) => (b & 0x0F) <= 9 && (b >> 4) <= 9;

    public static byte FromInt(int value)
    {
        if (value is < 0 or > 99) throw new ArgumentOutOfRangeException(nameof(value));
        return (byte)(((value / 10) << 4) | (value % 10));
    }

    public static int ToInt(byte b) => (b >> 4) * 10 + (b & 0x0F);
}
