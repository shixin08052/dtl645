using Dlt645.Core.DataItems;
using Dlt645.Core.Errors;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.Communication;

/// <summary>结果表格中的一行（界面显示与导出共用）。</summary>
public sealed class ReadResultRow
{
    public int Index { get; set; }
    public string Item { get; init; } = string.Empty;
    public string Field { get; init; } = string.Empty;
    public string Tariff { get; init; } = string.Empty;
    public string Period { get; init; } = string.Empty;
    public string Di { get; init; } = string.Empty;
    public string Value { get; init; } = string.Empty;
    public string Unit { get; init; } = string.Empty;
    public double? Number { get; init; }
    public string Raw { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public bool IsError { get; init; }
    public bool IsWarning { get; init; }
    public DateTime Time { get; init; } = DateTime.Now;
    public long ElapsedMs { get; init; }

    /// <summary>数据项名称 + 字段名，用于表格的“数据项”列。</summary>
    public string DisplayName => string.IsNullOrEmpty(Field) ? Item : $"{Item} - {Field}";

    public string TimeText => Time.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>把一次成功读取展开为若干行（每个字段一行）。</summary>
    public static IEnumerable<ReadResultRow> FromSuccess(ReadRequest req, DataResult res, IReadOnlyList<DecodedField> fields)
    {
        string status = "成功";
        bool warning = false;
        if (res.Warnings.Count > 0)
        {
            status = "成功（" + string.Join("；", res.Warnings.Select(w => w.Code.ToCodeString() + " " + w.Info.Title)) + "）";
            warning = true;
        }
        if (res.FrameCount > 1) status += $"（{res.FrameCount} 帧）";

        foreach (var f in fields)
        {
            yield return new ReadResultRow
            {
                Item = req.Item.Name,
                Field = f.Name,
                Tariff = req.TariffText,
                Period = req.PeriodText,
                Di = DataId.Format(res.Di),
                Value = f.Text,
                Unit = f.Unit,
                Number = f.Number,
                Raw = f.RawHex,
                Status = f.Valid ? status : "数据无效",
                IsWarning = warning || !f.Valid,
                ElapsedMs = (long)res.Elapsed.TotalMilliseconds,
            };
        }
    }

    public static ReadResultRow FromError(ReadRequest req, MeterException ex) => new()
    {
        Item = req.Item.Name,
        Tariff = req.TariffText,
        Period = req.PeriodText,
        Di = req.DiText,
        Value = "--",
        Status = StatusOf(ex),
        IsError = true,
    };

    /// <summary>错误状态文字，例如“E301 电表返回异常应答（Bit1 无请求数据）”。</summary>
    public static string StatusOf(MeterException ex) =>
        $"{ex.Code.ToCodeString()} {ex.Info.Title}" +
        (ex.MeterErrorWord is byte b ? $"（{string.Join("、", ErrorWord.Decode(b))}）" : string.Empty);
}
