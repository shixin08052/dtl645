using System.Globalization;

namespace Dlt645.Core.Protocol;

/// <summary>
/// 数据标识 DI3 DI2 DI1 DI0，以 uint 保存（DI3 为最高字节），
/// 帧内按 DI0 在前发送。
/// </summary>
public static class DataId
{
    public static string Format(uint di) => di.ToString("X8", CultureInfo.InvariantCulture);

    public static string FormatSpaced(uint di) =>
        $"{(byte)(di >> 24):X2} {(byte)(di >> 16):X2} {(byte)(di >> 8):X2} {(byte)di:X2}";

    public static byte Di0(uint di) => (byte)di;
    public static byte Di1(uint di) => (byte)(di >> 8);
    public static byte Di2(uint di) => (byte)(di >> 16);
    public static byte Di3(uint di) => (byte)(di >> 24);

    public static uint WithDi0(uint di, byte value) => (di & 0xFFFFFF00u) | value;
    public static uint WithDi1(uint di, byte value) => (di & 0xFFFF00FFu) | ((uint)value << 8);

    /// <summary>帧内顺序（未加 33H）：DI0, DI1, DI2, DI3。</summary>
    public static byte[] ToWireBytes(uint di) =>
        new[] { (byte)di, (byte)(di >> 8), (byte)(di >> 16), (byte)(di >> 24) };

    /// <summary>从帧内顺序（已减 33H）还原。</summary>
    public static uint FromWireBytes(ReadOnlySpan<byte> b) =>
        (uint)(b[0] | (b[1] << 8) | (b[2] << 16) | (b[3] << 24));

    /// <summary>解析用户输入的 DI（显示顺序 DI3..DI0），如 "00010000"、"00 01 00 00"、"0x00010000"。</summary>
    public static bool TryParse(string? text, out uint di, out string? error)
    {
        di = 0;
        error = null;
        var s = (text ?? string.Empty).Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        s = s.Replace(" ", "").Replace("-", "").Replace("_", "");
        if (s.Length != 8)
        {
            error = "数据标识应为 4 字节（8 位十六进制），例如 00010000";
            return false;
        }
        if (!uint.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out di))
        {
            error = "数据标识只能包含 0~9 和 A~F";
            return false;
        }
        return true;
    }
}
