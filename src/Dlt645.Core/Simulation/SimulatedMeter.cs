using System.Globalization;
using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.Simulation;

/// <summary>
/// 模拟电表：按 DL/T 645-2007 应答读通信地址、读数据、读后续数据。
/// 数据值根据配置表自动生成（可用 <see cref="SetData"/> 指定）。
/// 为了演示后续帧，数据超过 <see cref="MaxDataPerFrame"/> 字节时分帧应答。
/// </summary>
public sealed class SimulatedMeter
{
    private readonly DataItemCatalog _catalog;
    private readonly Dictionary<uint, byte[]> _overrides = new();
    private readonly Random _random = new(645);
    private readonly object _gate = new();
    private (uint Di, byte[] Remaining, byte NextSeq)? _pending;

    public SimulatedMeter(DataItemCatalog catalog, MeterAddress? address = null)
    {
        _catalog = catalog;
        Address = address ?? MeterAddress.Parse("000012345678");
    }

    public MeterAddress Address { get; set; }

    /// <summary>单帧最多携带的数据字节数（不含 DI）。</summary>
    public int MaxDataPerFrame { get; set; } = 48;

    /// <summary>指定某个 DI 的应答数据（低字节在前，未加 33H）。</summary>
    public void SetData(uint di, byte[] data)
    {
        lock (_gate) _overrides[di] = data;
    }

    /// <summary>处理一帧请求，返回应答字节（含 FE 前导）；不应答时返回 null。</summary>
    public byte[]? Handle(Dlt645Frame request)
    {
        lock (_gate) return HandleCore(request);
    }

    private byte[]? HandleCore(Dlt645Frame req)
    {
        var self = Address.ToFrameBytes();
        var reqAddr = MeterAddress.FromFrameBytes(req.Address);

        switch (req.Control)
        {
            case ControlCode.ReadAddress:
                return reqAddr.IsFullWildcard ? Build(ControlCode.ReadAddressOk, self) : null;

            case ControlCode.ReadData:
            {
                if (!reqAddr.Matches(Address)) return null;
                if (req.Data.Length < 4) return Build(ControlCode.ReadDataError, new byte[] { 0x01 });
                uint di = DataId.FromWireBytes(req.Data);
                var payload = GetData(di);
                if (payload is null) return Build(ControlCode.ReadDataError, new byte[] { 0x02 });

                var diBytes = DataId.ToWireBytes(di);
                if (payload.Length > MaxDataPerFrame)
                {
                    _pending = (di, payload[MaxDataPerFrame..], 1);
                    return Build(ControlCode.ReadDataOkMore, diBytes.Concat(payload[..MaxDataPerFrame]).ToArray());
                }
                _pending = null;
                return Build(ControlCode.ReadDataOk, diBytes.Concat(payload).ToArray());
            }

            case ControlCode.ReadFollow:
            {
                if (!reqAddr.Matches(Address)) return null;
                if (req.Data.Length < 5) return Build(ControlCode.ReadFollowError, new byte[] { 0x01 });
                uint di = DataId.FromWireBytes(req.Data);
                byte seq = req.Data[4];
                if (_pending is not { } p || p.Di != di || p.NextSeq != seq)
                    return Build(ControlCode.ReadFollowError, new byte[] { 0x02 });

                var chunk = p.Remaining.Take(MaxDataPerFrame).ToArray();
                var rest = p.Remaining.Skip(MaxDataPerFrame).ToArray();
                bool more = rest.Length > 0;
                _pending = more ? (di, rest, (byte)(seq + 1)) : null;
                var data = DataId.ToWireBytes(di).Concat(chunk).Append(seq).ToArray();
                return Build(more ? ControlCode.ReadFollowOkMore : ControlCode.ReadFollowOk, data);
            }

            default:
                // 只读软件不会发出其他命令；模拟表对未知命令回“其他错误”
                if (!reqAddr.Matches(Address)) return null;
                return Build((byte)(req.Control | 0xC0), new byte[] { 0x01 });
        }
    }

    private byte[] Build(byte control, byte[] data)
    {
        var frame = FrameCodec.Build(Address.ToFrameBytes(), control, data, preamble: false);
        // 真实电表应答前一般也带若干 FE
        return new byte[] { 0xFE, 0xFE }.Concat(frame).ToArray();
    }

    // ---------------------------------------------------------------- 数据生成

    private byte[]? GetData(uint di)
    {
        if (_overrides.TryGetValue(di, out var o)) return o;
        var match = _catalog.Find(di);
        if (match is null) return null;
        if (match.Tariff > 4) return null; // 模拟一块 4 费率电表
        var bytes = new List<byte>();
        for (int i = 0; i < match.Item.Fields.Count; i++)
            bytes.AddRange(Generate(match, match.Item.Fields[i], i));
        return bytes.ToArray();
    }

    private static readonly double[] TariffShare = { 1.0, 0.12, 0.33, 0.30, 0.25 };

    private byte[] Generate(CatalogMatch m, FieldDefinition f, int fieldIndex)
    {
        var fmt = f.Format;
        var now = DateTime.Now;
        int history = Math.Max(0, m.History);

        if (f.Sample is { } sample)
        {
            if (sample.Equals("now", StringComparison.OrdinalIgnoreCase) && fmt.Kind == FormatKind.DateTime)
                return fmt.EncodeDateTime(now);
            return fmt.Kind switch
            {
                FormatKind.Bcd => fmt.EncodeNumber(double.Parse(sample, CultureInfo.InvariantCulture), f.Signed),
                // 日期时间示例值按显示顺序书写的 BCD 数字，如 DDhh 写作 "0100"
                FormatKind.DateTime => Hex.Parse(sample).Reverse().ToArray(),
                _ => fmt.EncodeText(sample),
            };
        }

        switch (fmt.Kind)
        {
            case FormatKind.Digits:
                return fmt.EncodeText(Address.Text);
            case FormatKind.Ascii:
                return fmt.EncodeText("SIM");
            case FormatKind.Hex:
                return new byte[fmt.Length];
            case FormatKind.DateTime:
            {
                // 历史越久时间越早；同一记录中后面的字段（如结束时刻）稍晚
                var t = m.Item.Category == "冻结数据"
                    ? now.Date.AddDays(-history + 1)
                    : now.AddDays(-history * 9 - 1).AddMinutes(fieldIndex * 17);
                return fmt.EncodeDateTime(t);
            }
        }

        // 数值
        double value;
        int phase = Math.Max(0, DataId.Di1(m.Item.Di) - 1);
        string unit = f.Unit;
        string name = string.IsNullOrEmpty(f.Name) ? m.Item.Name : f.Name;
        int tariff = m.Tariff >= 0 ? m.Tariff : TariffFromFieldName(f.Name);
        double share = TariffShare[Math.Clamp(tariff, 0, 4)];

        switch (unit)
        {
            case "kWh" or "kvarh" or "kVAh":
            {
                double baseValue = 8000 + DataId.Di2(m.Item.Di) * 137.5;
                if (name.Contains("反向")) baseValue = 35.6 + DataId.Di2(m.Item.Di);
                value = baseValue * share * (1 - 0.035 * history);
                break;
            }
            case "V":
                value = name.Contains("电池") ? 3.62 : 220.0 + phase * 0.7 + Jitter(1.5);
                break;
            case "A":
                value = name.Contains("零线") ? 0.012 : 5.0 + phase * 0.35 + Jitter(0.2);
                break;
            case "kW":
                value = m.Item.Category == "最大需量" ? 3.5 * share * (1 - 0.03 * history) : 1.10 + phase * 0.05 + Jitter(0.03);
                break;
            case "kvar":
                value = m.Item.Category == "最大需量" ? 0.85 * share : 0.21 + Jitter(0.02);
                break;
            case "kVA":
                value = m.Item.Category == "最大需量" ? 3.7 * share : 1.13 + Jitter(0.03);
                break;
            case "Hz":
                value = 50.0 + Jitter(0.03);
                break;
            case "℃":
                value = 31.5 + Jitter(0.5);
                break;
            case "°":
                value = phase * 120 + 12.3;
                break;
            case "次":
                value = 3 + DataId.Di1(m.Item.Di) + fieldIndex;
                break;
            case "分钟":
                value = name.Contains("电池") ? 1234 : 45 + fieldIndex * 6;
                break;
            default:
                value = name.Contains("功率因数") ? 0.975 + Jitter(0.01) : 0;
                break;
        }
        return fmt.EncodeNumber(value, f.Signed);
    }

    private static int TariffFromFieldName(string name) => name switch
    {
        "尖" => 1,
        "峰" => 2,
        "平" => 3,
        "谷" => 4,
        _ => 0,
    };

    private double Jitter(double amplitude) => (_random.NextDouble() * 2 - 1) * amplitude;
}
