using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

/// <summary>一次读取请求：数据项 + 费率 + 历史序号。</summary>
public sealed record ReadRequest(DataItemDefinition Item, int Tariff = -1, int History = -1)
{
    public uint Di => Item.ComposeDi(Tariff, History);
    public string DiText => DataId.Format(Di);
    public string TariffText => Item.Tariffs ? Tariffs.Name(Tariff) : string.Empty;
    public string PeriodText => Item.FormatHistory(History);

    public string Describe()
    {
        var parts = new List<string> { Item.Name };
        if (TariffText.Length > 0) parts.Add(TariffText);
        if (PeriodText.Length > 0) parts.Add(PeriodText);
        return string.Join("·", parts);
    }

    /// <summary>
    /// 展开：数据项 × 费率 × 历史序号。
    /// 月份历史使用 <paramref name="months"/>（0=当前，1~12=上N月）；
    /// 次数历史读取第 1 ~ <paramref name="lastTimes"/> 次。
    /// </summary>
    public static IEnumerable<ReadRequest> Expand(
        IEnumerable<DataItemDefinition> items,
        IReadOnlyCollection<int> tariffs,
        IReadOnlyCollection<int> months,
        int lastTimes)
    {
        foreach (var item in items)
        {
            IEnumerable<int> tList = item.Tariffs ? (tariffs.Count > 0 ? tariffs : new[] { 0 }) : new[] { -1 };
            IEnumerable<int> hList = item.History switch
            {
                HistoryKind.Month => (months.Count > 0 ? months : new[] { 0 }).Where(m => m <= item.HistoryMax),
                HistoryKind.Times => Enumerable.Range(1, Math.Clamp(lastTimes, 1, Math.Max(1, item.HistoryMax))),
                _ => new[] { -1 },
            };
            var hs = hList.ToList();
            foreach (var t in tList)
            foreach (var h in hs)
                yield return new ReadRequest(item, t, h);
        }
    }
}
