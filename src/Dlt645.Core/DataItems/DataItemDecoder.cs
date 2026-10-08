using System.Text;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

/// <summary>一个字段的解析结果。</summary>
public sealed record DecodedField(string Name, string Text, string Unit, double? Number, bool Valid, string RawHex);

/// <summary>按配置表把应答数据解析为可显示的字段。</summary>
public static class DataItemDecoder
{
    public static IReadOnlyList<DecodedField> Decode(DataItemDefinition item, ReadOnlySpan<byte> data) =>
        Decode(item.Fields, data);

    public static IReadOnlyList<DecodedField> Decode(IReadOnlyList<FieldDefinition> fields, ReadOnlySpan<byte> data)
    {
        var result = new List<DecodedField>(fields.Count + 1);
        int offset = 0;
        foreach (var f in fields)
        {
            if (offset + f.Length > data.Length)
            {
                int remain = Math.Max(0, data.Length - offset);
                result.Add(new DecodedField(f.Name, offset >= data.Length ? "（无数据）" : $"数据长度不足（需要 {f.Length} 字节，剩余 {remain} 字节）",
                    f.Unit, null, false, remain > 0 ? Hex.ToHex(data[offset..], "") : string.Empty));
                offset = data.Length;
                continue;
            }
            var slice = data.Slice(offset, f.Length);
            var o = f.Format.Decode(slice, f.Signed);
            result.Add(new DecodedField(f.Name, o.Text, f.Unit, o.Number, o.Valid, Hex.ToHex(slice, "")));
            offset += f.Length;
        }
        if (offset < data.Length)
        {
            var rest = data[offset..];
            result.Add(new DecodedField($"其余数据（{rest.Length}字节，未定义）", Hex.ToHexReversed(rest, " "), string.Empty, null, true,
                Hex.ToHex(rest, "")));
        }
        return result;
    }

    /// <summary>用指定格式解析（自定义 DI 页使用）。</summary>
    public static IReadOnlyList<DecodedField> DecodeWithFormat(string formatSpec, bool signed, ReadOnlySpan<byte> data)
    {
        var fmt = DataFormat.Parse(formatSpec);
        var fields = new List<FieldDefinition>();
        int count = Math.Max(1, data.Length / Math.Max(1, fmt.Length));
        for (int i = 0; i < count; i++)
            fields.Add(new FieldDefinition(count > 1 ? $"第{i + 1}项" : string.Empty, formatSpec, null, signed, null));
        return Decode(fields, data);
    }

    /// <summary>未知数据项时的尝试性解析：给出高字节在前的 HEX、BCD 数值和 ASCII。</summary>
    public static IReadOnlyList<DecodedField> AutoInterpret(ReadOnlySpan<byte> data)
    {
        var list = new List<DecodedField>();
        var raw = Hex.ToHex(data, "");
        if (data.IsEmpty)
        {
            list.Add(new DecodedField("数据", "（应答中无数据）", string.Empty, null, true, raw));
            return list;
        }
        var msb = data.ToArray();
        Array.Reverse(msb);
        list.Add(new DecodedField("高字节在前", Hex.ToHex(msb, " "), string.Empty, null, true, raw));
        if (msb.All(Bcd.IsValid))
        {
            var digits = Hex.ToHex(msb, "").TrimStart('0');
            list.Add(new DecodedField("按 BCD 整数", digits.Length == 0 ? "0" : digits, string.Empty, null, true, raw));
        }
        if (msb.All(b => b is >= 0x20 and < 0x7F or 0x00))
        {
            var text = Encoding.ASCII.GetString(msb.Where(b => b != 0).ToArray()).Trim();
            if (text.Length > 0) list.Add(new DecodedField("按 ASCII", text, string.Empty, null, true, raw));
        }
        return list;
    }
}
