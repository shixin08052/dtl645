namespace Dlt645.Core.Protocol;

/// <summary>
/// 表计通信地址（表号）。<see cref="Text"/> 为正常显示顺序的 12 位字符串，
/// 帧中按 A0（最低两位）在前的顺序发送，每字节为 BCD 码，AAH 表示通配。
/// </summary>
public sealed class MeterAddress : IEquatable<MeterAddress>
{
    public const string WildcardText = "AAAAAAAAAAAA";
    public static MeterAddress Wildcard { get; } = new(WildcardText);

    public string Text { get; }

    private MeterAddress(string text) => Text = text;

    /// <summary>全部为 AA 的公共地址。</summary>
    public bool IsFullWildcard => Text == WildcardText;

    /// <summary>包含 AA 通配字节。</summary>
    public bool HasWildcard => ToFrameBytes().Any(b => b == 0xAA);

    /// <summary>转换为帧内顺序 A0..A5。</summary>
    public byte[] ToFrameBytes()
    {
        var bytes = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            bytes[i] = Convert.ToByte(Text.Substring(10 - 2 * i, 2), 16);
        }
        return bytes;
    }

    public static MeterAddress FromFrameBytes(ReadOnlySpan<byte> a0ToA5)
    {
        if (a0ToA5.Length < 6) throw new ArgumentException("地址需要 6 字节", nameof(a0ToA5));
        return new MeterAddress(Hex.ToHexReversed(a0ToA5[..6]));
    }

    /// <summary>
    /// 请求地址与应答地址是否匹配：请求地址中 AA 的字节视为通配，其余必须一致。
    /// </summary>
    public bool Matches(MeterAddress actual)
    {
        var req = ToFrameBytes();
        var act = actual.ToFrameBytes();
        for (int i = 0; i < 6; i++)
        {
            if (req[i] != 0xAA && req[i] != act[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// 解析用户输入的表号：允许 12 位以内数字（不足自动左补 0），
    /// 或 AA 通配（全部为 A 时视为公共地址 AAAAAAAAAAAA；部分通配时 AA 必须按字节成对出现）。
    /// </summary>
    public static bool TryParse(string? input, out MeterAddress? address, out string? error)
    {
        address = null;
        error = null;
        var s = (input ?? string.Empty).Replace(" ", "").Trim().ToUpperInvariant();
        if (s.Length == 0)
        {
            error = "表号不能为空";
            return false;
        }
        if (s.Length > 12)
        {
            error = $"表号最多 12 位，当前输入了 {s.Length} 位";
            return false;
        }
        if (s.All(c => c == 'A'))
        {
            address = Wildcard;
            return true;
        }
        if (s.Any(c => !char.IsAsciiDigit(c) && c != 'A'))
        {
            error = "表号只能包含数字 0~9，或用 AA 表示通配";
            return false;
        }
        s = s.PadLeft(12, '0');
        for (int i = 0; i < 12; i += 2)
        {
            var pair = s.Substring(i, 2);
            bool digits = char.IsAsciiDigit(pair[0]) && char.IsAsciiDigit(pair[1]);
            if (!digits && pair != "AA")
            {
                error = "通配符 A 必须以 AA 成对出现（每字节两位）";
                return false;
            }
        }
        address = new MeterAddress(s);
        return true;
    }

    public static MeterAddress Parse(string input) =>
        TryParse(input, out var a, out var e) ? a! : throw new FormatException(e);

    public bool Equals(MeterAddress? other) => other is not null && other.Text == Text;
    public override bool Equals(object? obj) => obj is MeterAddress m && Equals(m);
    public override int GetHashCode() => Text.GetHashCode();
    public override string ToString() => Text;
}
