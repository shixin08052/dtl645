using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

public enum FormatKind
{
    /// <summary>BCD 数值，如 XXXXXX.XX、XXX.X、NNNNNN。</summary>
    Bcd,
    /// <summary>日期时间，由 YY MM DD WW hh mm ss 组合，如 YYMMDDhhmm。</summary>
    DateTime,
    /// <summary>ASCII 字符串，ASCII:n。</summary>
    Ascii,
    /// <summary>原样十六进制显示（高字节在前），HEX:n。</summary>
    Hex,
    /// <summary>数字串（保留前导 0，如表号），NNNNNNNNNNNN 或 DIGITS:n。</summary>
    Digits,
}

/// <summary>解码结果。</summary>
public readonly record struct DecodeOutput(string Text, double? Number, bool Valid);

/// <summary>
/// 数据格式。规约中所有数据项均为低字节在前传输，本类负责“反转 → 取 BCD → 处理小数位/符号位”。
/// </summary>
public sealed partial class DataFormat
{
    private static readonly ConcurrentDictionary<string, DataFormat> Cache = new();
    private static readonly string[] DateTokens = { "YY", "MM", "DD", "WW", "hh", "mm", "ss" };
    private static readonly string[] WeekNames = { "日", "一", "二", "三", "四", "五", "六" };

    private DataFormat(string spec, FormatKind kind, int length, int decimals = 0, IReadOnlyList<string>? tokens = null)
    {
        Spec = spec;
        Kind = kind;
        Length = length;
        Decimals = decimals;
        Tokens = tokens ?? Array.Empty<string>();
    }

    public string Spec { get; }
    public FormatKind Kind { get; }
    /// <summary>字节数。</summary>
    public int Length { get; }
    /// <summary>小数位数（仅 BCD）。</summary>
    public int Decimals { get; }
    /// <summary>日期时间的组成部分（仅 DateTime）。</summary>
    public IReadOnlyList<string> Tokens { get; }

    public static DataFormat Parse(string spec) =>
        TryParse(spec, out var f, out var e) ? f! : throw new FormatException(e);

    public static bool TryParse(string? spec, out DataFormat? format, out string? error)
    {
        format = null;
        error = null;
        spec = spec?.Trim() ?? string.Empty;
        if (spec.Length == 0)
        {
            error = "格式为空";
            return false;
        }
        if (Cache.TryGetValue(spec, out format)) return true;

        format = Create(spec, out error);
        if (format is null) return false;
        Cache[spec] = format;
        return true;
    }

    private static DataFormat? Create(string spec, out string? error)
    {
        error = null;
        var prefixed = PrefixedRegex().Match(spec);
        if (prefixed.Success)
        {
            int n = int.Parse(prefixed.Groups[2].Value, CultureInfo.InvariantCulture);
            if (n is <= 0 or > 200)
            {
                error = $"长度 {n} 超出范围";
                return null;
            }
            var kind = prefixed.Groups[1].Value.ToUpperInvariant() switch
            {
                "ASCII" => FormatKind.Ascii,
                "HEX" => FormatKind.Hex,
                _ => FormatKind.Digits,
            };
            return new DataFormat(spec, kind, n);
        }

        if (spec.Length % 2 == 0 && IsDateSpec(spec))
        {
            var tokens = Enumerable.Range(0, spec.Length / 2).Select(i => spec.Substring(i * 2, 2)).ToList();
            return new DataFormat(spec, FormatKind.DateTime, tokens.Count, tokens: tokens);
        }

        var m = BcdRegex().Match(spec);
        if (m.Success)
        {
            int digits = spec.Count(c => c is 'X' or 'N');
            if (digits % 2 != 0)
            {
                error = $"格式 {spec} 的位数必须为偶数";
                return null;
            }
            int decimals = m.Groups[2].Success ? m.Groups[2].Value.Length - 1 : 0;
            // 12 位纯 N（表号/通信地址）保留前导 0
            if (decimals == 0 && digits == 12 && spec.All(c => c == 'N'))
                return new DataFormat(spec, FormatKind.Digits, 6);
            return new DataFormat(spec, FormatKind.Bcd, digits / 2, decimals);
        }

        error = $"无法识别的数据格式：{spec}";
        return null;
    }

    private static bool IsDateSpec(string spec)
    {
        for (int i = 0; i < spec.Length; i += 2)
        {
            if (!DateTokens.Contains(spec.Substring(i, 2), StringComparer.Ordinal)) return false;
        }
        return true;
    }

    [GeneratedRegex(@"^(ASCII|HEX|DIGITS):(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex PrefixedRegex();

    [GeneratedRegex(@"^([XN]+)(\.[XN]+)?$")]
    private static partial Regex BcdRegex();

    // ------------------------------------------------------------------ 解码

    /// <summary>解码一段数据（低字节在前，已减 33H）。</summary>
    public DecodeOutput Decode(ReadOnlySpan<byte> data, bool signed = false)
    {
        if (data.Length < Length)
            return new DecodeOutput($"数据长度不足（需要 {Length} 字节，实际 {data.Length} 字节）", null, false);

        var msbFirst = data[..Length].ToArray();
        Array.Reverse(msbFirst);

        switch (Kind)
        {
            case FormatKind.Ascii:
                return new DecodeOutput(DecodeAscii(msbFirst), null, true);
            case FormatKind.Hex:
                return new DecodeOutput(Hex.ToHex(msbFirst, ""), null, true);
            case FormatKind.Digits:
                return new DecodeOutput(Hex.ToHex(msbFirst, ""), null, true);
        }

        if (msbFirst.All(b => b == 0xFF))
            return new DecodeOutput("--（无效数据 FF）", null, false);

        if (Kind == FormatKind.DateTime)
            return DecodeDateTime(msbFirst);

        // BCD 数值
        bool negative = false;
        if (signed && (msbFirst[0] & 0x80) != 0)
        {
            negative = true;
            msbFirst[0] &= 0x7F;
        }
        if (!msbFirst.All(Bcd.IsValid))
            return new DecodeOutput($"非 BCD 数据：{Hex.ToHex(data[..Length], "")}", null, false);

        var digits = Hex.ToHex(msbFirst, "");
        decimal value = decimal.Parse(digits, CultureInfo.InvariantCulture);
        for (int i = 0; i < Decimals; i++) value /= 10m;
        if (negative) value = -value;
        var text = value.ToString("F" + Decimals, CultureInfo.InvariantCulture);
        return new DecodeOutput(text, (double)value, true);
    }

    private DecodeOutput DecodeDateTime(byte[] msbFirst)
    {
        if (!msbFirst.All(Bcd.IsValid))
            return new DecodeOutput($"非 BCD 时间：{Hex.ToHex(msbFirst, "")}", null, false);

        var v = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < Tokens.Count; i++) v[Tokens[i]] = msbFirst[i].ToString("X2");

        var parts = new List<string>();
        bool hasY = v.ContainsKey("YY"), hasMo = v.ContainsKey("MM"), hasD = v.ContainsKey("DD");
        if (hasY || hasMo)
        {
            var date = new List<string>();
            if (hasY) date.Add("20" + v["YY"]);
            if (hasMo) date.Add(v["MM"]);
            if (hasD) date.Add(v["DD"]);
            parts.Add(string.Join("-", date));
        }
        else if (hasD)
        {
            parts.Add($"{v["DD"]}日");
        }

        if (v.TryGetValue("WW", out var w))
        {
            int wi = Bcd.ToInt(Convert.ToByte(w, 16));
            parts.Add(wi is >= 0 and <= 6 ? "星期" + WeekNames[wi] : $"星期?({w})");
        }

        bool hasH = v.ContainsKey("hh"), hasMi = v.ContainsKey("mm"), hasS = v.ContainsKey("ss");
        if (hasH && !hasMi && !hasS)
        {
            parts.Add($"{v["hh"]}时");
        }
        else if (hasH || hasMi || hasS)
        {
            var time = new List<string>();
            if (hasH) time.Add(v["hh"]);
            if (hasMi) time.Add(v["mm"]);
            if (hasS) time.Add(v["ss"]);
            parts.Add(string.Join(":", time));
        }
        return new DecodeOutput(string.Join(" ", parts), null, true);
    }

    private static string DecodeAscii(byte[] msbFirst)
    {
        var sb = new StringBuilder(msbFirst.Length);
        foreach (var b in msbFirst)
        {
            if (b is 0x00 or 0xFF) continue;
            sb.Append(b is >= 0x20 and < 0x7F ? (char)b : '.');
        }
        var s = sb.ToString().Trim();
        return s.Length == 0 ? "（空）" : s;
    }

    // ------------------------------------------------------------------ 编码（模拟电表与单元测试使用）

    public byte[] EncodeNumber(double value, bool signed = false)
    {
        if (Kind != FormatKind.Bcd) throw new InvalidOperationException($"{Spec} 不是数值格式");
        decimal scaled = Math.Round(Math.Abs((decimal)value) * Pow10(Decimals), 0, MidpointRounding.AwayFromZero);
        var digits = ((long)scaled).ToString(CultureInfo.InvariantCulture);
        if (digits.Length > Length * 2) digits = digits[^(Length * 2)..];
        digits = digits.PadLeft(Length * 2, '0');
        var msbFirst = Hex.Parse(digits);
        if (signed && value < 0) msbFirst[0] |= 0x80;
        Array.Reverse(msbFirst);
        return msbFirst;
    }

    public byte[] EncodeDateTime(System.DateTime t)
    {
        if (Kind != FormatKind.DateTime) throw new InvalidOperationException($"{Spec} 不是日期时间格式");
        var msbFirst = Tokens.Select(tok => Bcd.FromInt(tok switch
        {
            "YY" => t.Year % 100,
            "MM" => t.Month,
            "DD" => t.Day,
            "WW" => (int)t.DayOfWeek,
            "hh" => t.Hour,
            "mm" => t.Minute,
            _ => t.Second,
        })).ToArray();
        Array.Reverse(msbFirst);
        return msbFirst;
    }

    /// <summary>编码文本：ASCII 字符串，或 HEX/数字串（高字节在前书写）。</summary>
    public byte[] EncodeText(string text)
    {
        byte[] msbFirst;
        if (Kind == FormatKind.Ascii)
        {
            msbFirst = new byte[Length];
            var raw = Encoding.ASCII.GetBytes(text);
            Array.Copy(raw, msbFirst, Math.Min(raw.Length, Length));
        }
        else if (Kind is FormatKind.Hex or FormatKind.Digits)
        {
            var hex = text.Replace(" ", "").PadLeft(Length * 2, '0');
            msbFirst = Hex.Parse(hex[^(Length * 2)..]);
        }
        else
        {
            throw new InvalidOperationException($"{Spec} 不是文本格式");
        }
        Array.Reverse(msbFirst);
        return msbFirst;
    }

    private static decimal Pow10(int n)
    {
        decimal r = 1;
        for (int i = 0; i < n; i++) r *= 10;
        return r;
    }

    public override string ToString() => Spec;
}
