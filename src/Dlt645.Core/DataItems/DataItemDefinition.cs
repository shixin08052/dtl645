using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

/// <summary>历史数据的索引方式（占用 DI0）。</summary>
public enum HistoryKind
{
    /// <summary>无历史，DI0 固定。</summary>
    None,
    /// <summary>结算日/月份：DI0 = 00 当前，01~0C 上 1~12 月（结算日）。</summary>
    Month,
    /// <summary>上 N 次记录：DI0 = 01~N。</summary>
    Times,
}

/// <summary>数据项中的一个字段（例如最大需量项包含“需量值”和“发生时间”两个字段）。</summary>
public sealed class FieldDefinition
{
    public FieldDefinition(string name, string format, string? unit, bool signed, string? sample)
    {
        Name = name;
        Format = DataFormat.Parse(format);
        Unit = unit ?? string.Empty;
        Signed = signed;
        Sample = sample;
    }

    /// <summary>字段名；只有一个字段时可为空，此时显示数据项名称。</summary>
    public string Name { get; }
    public DataFormat Format { get; }
    public string Unit { get; }
    /// <summary>最高位为符号位。</summary>
    public bool Signed { get; }
    /// <summary>模拟电表使用的示例值（可选）。</summary>
    public string? Sample { get; }
    public int Length => Format.Length;
}

/// <summary>配置表中的一个数据项。</summary>
public sealed class DataItemDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Category { get; init; }
    public string Group { get; init; } = string.Empty;

    /// <summary>基础数据标识。带费率/历史的数据项，DI1/DI0 会在读取时替换。</summary>
    public required uint Di { get; init; }

    public required IReadOnlyList<FieldDefinition> Fields { get; init; }

    /// <summary>DI1 表示费率（00 总，01~04 尖峰平谷）。</summary>
    public bool Tariffs { get; init; }

    public HistoryKind History { get; init; }

    /// <summary>历史数据的最大序号（Month 默认 12）。</summary>
    public int HistoryMax { get; init; }

    /// <summary>历史序号的显示格式，如“上{0}月”“上{0}结算日”“上{0}次”。</summary>
    public string HistoryLabel { get; init; } = "上{0}次";

    /// <summary>参与“一键读取常用数据”。</summary>
    public bool Common { get; init; }

    /// <summary>一键读取时要读的费率序号。</summary>
    public IReadOnlyList<int> CommonTariffs { get; init; } = new[] { 0 };

    public string Note { get; init; } = string.Empty;

    public int TotalLength => Fields.Sum(f => f.Length);

    public string DiText => DataId.Format(Di);

    /// <summary>根据费率和历史序号计算实际 DI。</summary>
    public uint ComposeDi(int tariff, int history)
    {
        uint di = Di;
        if (Tariffs && tariff >= 0) di = DataId.WithDi1(di, (byte)tariff);
        if (History != HistoryKind.None && history >= 0) di = DataId.WithDi0(di, (byte)history);
        return di;
    }

    /// <summary>判断某个 DI 是否属于本数据项，返回费率号和历史序号。</summary>
    public bool TryMatch(uint di, out int tariff, out int history)
    {
        tariff = -1;
        history = -1;
        uint mask = 0xFFFFFFFF;
        if (Tariffs) mask &= 0xFFFF00FF;
        if (History != HistoryKind.None) mask &= 0xFFFFFF00;
        if ((di & mask) != (Di & mask)) return false;

        if (Tariffs)
        {
            tariff = DataId.Di1(di);
            if (tariff > Dlt645.Core.DataItems.Tariffs.MaxTariff) return false;
        }
        if (History != HistoryKind.None)
        {
            history = DataId.Di0(di);
            int min = History == HistoryKind.Month ? 0 : 1;
            if (history < min || history > HistoryMax) return false;
        }
        return true;
    }

    public string FormatHistory(int history)
    {
        if (History == HistoryKind.None || history < 0) return string.Empty;
        if (History == HistoryKind.Month && history == 0) return "当前";
        return string.Format(HistoryLabel, history);
    }

    public override string ToString() => $"{Name} ({DiText})";
}

public static class Tariffs
{
    /// <summary>国内习惯：费率 1~4 依次为尖、峰、平、谷。</summary>
    public static IReadOnlyList<string> Names { get; } = new[] { "总", "尖", "峰", "平", "谷" };

    public const int MaxTariff = 14;

    public static string Name(int tariff) =>
        tariff < 0 ? string.Empty : tariff < Names.Count ? Names[tariff] : $"费率{tariff}";
}
