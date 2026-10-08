using System.Globalization;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.DataItems;

/// <summary>每月结算日（DL/T 645-2007：04 00 0B 01~03，格式 DDhh）。</summary>
public sealed record SettlementDay(int Day, int Hour)
{
    public override string ToString() => $"{Day}日{Hour}时";
}

/// <summary>
/// 结算日历：根据电表的结算日和电表当前时间，算出“上 N 结算日（上 N 月）”对应的具体结算时刻。
/// 例如结算日为每月 1 日 0 时、电表时间 2026-10-08 时：上1月 = 2026-10-01 00:00，上2月 = 2026-09-01 00:00。
/// 有多个结算日时，按时间倒序合并计数（上1结算日为最近一次结算）。
/// </summary>
public sealed class SettlementCalendar
{
    public const uint SettlementDay1 = 0x04000B01;
    public const uint SettlementDay2 = 0x04000B02;
    public const uint SettlementDay3 = 0x04000B03;
    public const uint MeterDate = 0x04000101;
    public const uint MeterTime = 0x04000102;

    public SettlementCalendar(IReadOnlyList<SettlementDay> days, DateTime meterNow, bool fromMeterClock)
    {
        if (days.Count == 0) throw new ArgumentException("至少需要一个结算日", nameof(days));
        Days = days.Distinct().OrderBy(d => d.Day).ThenBy(d => d.Hour).ToList();
        MeterNow = meterNow;
        FromMeterClock = fromMeterClock;
    }

    public IReadOnlyList<SettlementDay> Days { get; }

    /// <summary>计算所用的“当前时间”（优先使用电表时钟）。</summary>
    public DateTime MeterNow { get; }

    /// <summary>MeterNow 来自电表时钟（false 表示读不到电表时间，用的是电脑时间）。</summary>
    public bool FromMeterClock { get; }

    /// <summary>第 n 个最近的结算时刻（n ≥ 1）。</summary>
    public DateTime? PointFor(int n)
    {
        if (n < 1) return null;
        int found = 0;
        // 每月至少一个结算日，往前最多查 n+2 个月即可；结算日为 29~31 日时部分月份没有，多留余量
        for (int back = 0; back <= n * 2 + 3; back++)
        {
            var month = new DateTime(MeterNow.Year, MeterNow.Month, 1).AddMonths(-back);
            int daysInMonth = DateTime.DaysInMonth(month.Year, month.Month);
            foreach (var d in Days.OrderByDescending(x => x.Day).ThenByDescending(x => x.Hour))
            {
                if (d.Day > daysInMonth) continue;
                var point = new DateTime(month.Year, month.Month, d.Day, d.Hour, 0, 0);
                if (point > MeterNow) continue;
                if (++found == n) return point;
            }
        }
        return null;
    }

    /// <summary>结算日期的显示文字，如 “2026-10-01” 或 “2026-10-15 12时”。</summary>
    public string? DateTextFor(int n)
    {
        var p = PointFor(n);
        if (p is null) return null;
        return p.Value.Hour == 0
            ? p.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : p.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + $" {p.Value.Hour}时";
    }

    /// <summary>月份选择器上的短文字，如 “10-01”。</summary>
    public string? ShortTextFor(int n) => PointFor(n)?.ToString("MM-dd", CultureInfo.InvariantCulture);

    public string Describe() =>
        "每月" + string.Join("、", Days.Select(d => d.ToString())) +
        (FromMeterClock ? $"（电表时间 {MeterNow:yyyy-MM-dd HH:mm}）" : $"（未读到电表时间，按电脑时间 {MeterNow:yyyy-MM-dd HH:mm} 计算）");

    /// <summary>
    /// 解析结算日数据（DDhh，低字节在前：[hh, DD]）。
    /// DD 必须为 01~28（规约范围，放宽到 31），hh 为 00~23；“9999”“0000”等表示未设置。
    /// </summary>
    public static bool TryParseDay(ReadOnlySpan<byte> data, out SettlementDay? day)
    {
        day = null;
        if (data.Length < 2 || !Bcd.IsValid(data[0]) || !Bcd.IsValid(data[1])) return false;
        int hh = Bcd.ToInt(data[0]);
        int dd = Bcd.ToInt(data[1]);
        if (dd is < 1 or > 31 || hh > 23) return false;
        day = new SettlementDay(dd, hh);
        return true;
    }

    /// <summary>解析电表日期（YYMMDDWW，低字节在前：[WW, DD, MM, YY]）与时间（hhmmss：[ss, mm, hh]）。</summary>
    public static bool TryParseMeterClock(ReadOnlySpan<byte> date, ReadOnlySpan<byte> time, out DateTime value)
    {
        value = default;
        if (date.Length < 4 || time.Length < 3) return false;
        for (int i = 0; i < 4; i++) if (!Bcd.IsValid(date[i])) return false;
        for (int i = 0; i < 3; i++) if (!Bcd.IsValid(time[i])) return false;
        int yy = Bcd.ToInt(date[3]), mo = Bcd.ToInt(date[2]), dd = Bcd.ToInt(date[1]);
        int hh = Bcd.ToInt(time[2]), mi = Bcd.ToInt(time[1]), ss = Bcd.ToInt(time[0]);
        if (mo is < 1 or > 12 || dd < 1 || dd > DateTime.DaysInMonth(2000 + yy, mo) || hh > 23 || mi > 59 || ss > 59) return false;
        value = new DateTime(2000 + yy, mo, dd, hh, mi, ss);
        return true;
    }
}
