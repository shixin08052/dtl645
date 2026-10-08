using Dlt645.Core.Communication;
using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;
using Dlt645.Core.Simulation;

namespace Dlt645.Core.Tests;

public class SettlementCalendarTests
{
    private static SettlementCalendar Cal(DateTime now, params (int Day, int Hour)[] days) =>
        new(days.Select(d => new SettlementDay(d.Day, d.Hour)).ToList(), now, true);

    [Fact]
    public void FirstOfMonth_MapsMonthsBackwards()
    {
        var c = Cal(new DateTime(2026, 10, 8, 16, 30, 0), (1, 0));
        Assert.Equal(new DateTime(2026, 10, 1), c.PointFor(1));
        Assert.Equal(new DateTime(2026, 9, 1), c.PointFor(2));
        Assert.Equal(new DateTime(2025, 11, 1), c.PointFor(12));
        Assert.Equal("2026-10-01", c.DateTextFor(1));
        Assert.Equal("10-01", c.ShortTextFor(1));
    }

    [Fact]
    public void SettlementNotYetReachedThisMonth_UsesPreviousMonth()
    {
        var c = Cal(new DateTime(2026, 10, 8), (15, 12));
        Assert.Equal(new DateTime(2026, 9, 15, 12, 0, 0), c.PointFor(1));
        Assert.Equal("2026-09-15 12时", c.DateTextFor(1));
    }

    [Fact]
    public void ExactlyAtSettlement_CountsAsSettled()
    {
        var c = Cal(new DateTime(2026, 10, 15, 12, 0, 0), (15, 12));
        Assert.Equal(new DateTime(2026, 10, 15, 12, 0, 0), c.PointFor(1));
    }

    [Fact]
    public void MultipleSettlementDays_AreMerged()
    {
        var c = Cal(new DateTime(2026, 10, 8), (1, 0), (15, 0));
        Assert.Equal(new DateTime(2026, 10, 1), c.PointFor(1));
        Assert.Equal(new DateTime(2026, 9, 15), c.PointFor(2));
        Assert.Equal(new DateTime(2026, 9, 1), c.PointFor(3));
    }

    [Fact]
    public void YearBoundary()
    {
        var c = Cal(new DateTime(2026, 1, 5), (1, 0));
        Assert.Equal(new DateTime(2026, 1, 1), c.PointFor(1));
        Assert.Equal(new DateTime(2025, 12, 1), c.PointFor(2));
    }

    [Theory]
    [InlineData("00 01", true, 1, 0)]     // 每月1日0时：低字节在前 [hh, DD]
    [InlineData("12 15", true, 15, 12)]
    [InlineData("99 99", false, 0, 0)]   // 未设置
    [InlineData("00 00", false, 0, 0)]
    [InlineData("25 01", false, 0, 0)]   // 小时超范围
    public void ParseDay(string hex, bool ok, int day, int hour)
    {
        Assert.Equal(ok, SettlementCalendar.TryParseDay(Hex.Parse(hex), out var d));
        if (ok) Assert.Equal(new SettlementDay(day, hour), d);
    }

    [Fact]
    public void ParseMeterClock()
    {
        // 2026-10-08 星期四 16:30:05
        Assert.True(SettlementCalendar.TryParseMeterClock(Hex.Parse("04 08 10 26"), Hex.Parse("05 30 16"), out var t));
        Assert.Equal(new DateTime(2026, 10, 8, 16, 30, 5), t);
        Assert.False(SettlementCalendar.TryParseMeterClock(Hex.Parse("04 32 10 26"), Hex.Parse("05 30 16"), out _));
    }

    [Fact]
    public async Task Simulator_ProvidesSettlementDayAndClock()
    {
        var catalog = DataItemCatalog.LoadBuiltIn();
        var t = new SimulatedTransport(new SimulatedMeter(catalog)) { ResponseDelayMs = 5 };
        t.Open();
        var client = new MeterClient(t, new CommOptions { TimeoutMs = 300, Retries = 0 });
        var day = await client.ReadDataAsync(MeterAddress.Wildcard, SettlementCalendar.SettlementDay1);
        Assert.True(SettlementCalendar.TryParseDay(day.Data, out var d));
        Assert.Equal(new SettlementDay(1, 0), d);
        var date = await client.ReadDataAsync(MeterAddress.Wildcard, SettlementCalendar.MeterDate);
        var time = await client.ReadDataAsync(MeterAddress.Wildcard, SettlementCalendar.MeterTime);
        Assert.True(SettlementCalendar.TryParseMeterClock(date.Data, time.Data, out var now));
        Assert.True(Math.Abs((now - DateTime.Now).TotalMinutes) < 2);
    }
}
