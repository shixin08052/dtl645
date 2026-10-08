using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;

namespace Dlt645.Core.Tests;

public class CatalogTests
{
    private static readonly DataItemCatalog Catalog = DataItemCatalog.LoadBuiltIn();

    [Fact]
    public void BuiltIn_LoadsAllCategories()
    {
        Assert.Contains("电能量", Catalog.Categories);
        Assert.Contains("最大需量", Catalog.Categories);
        Assert.Contains("实时变量", Catalog.Categories);
        Assert.Contains("电表参数", Catalog.Categories);
        Assert.Contains("事件记录", Catalog.Categories);
        Assert.Contains("冻结数据", Catalog.Categories);
        Assert.NotEmpty(Catalog.CommonItems);
    }

    [Theory]
    [InlineData(0, 0x00010000u)]
    [InlineData(1, 0x00010001u)]
    [InlineData(12, 0x0001000Cu)]
    public void MonthSelector_MapsToDi0(int month, uint expected)
    {
        var item = Catalog.Items.Single(i => i.Id == "E.FwdActive");
        Assert.Equal(expected, item.ComposeDi(0, month));
    }

    [Fact]
    public void TariffAndMonth_ComposeDi()
    {
        var item = Catalog.Items.Single(i => i.Id == "E.FwdActive");
        // 正向有功 谷（费率4） 上3月 => 00 01 04 03
        Assert.Equal(0x00010403u, item.ComposeDi(4, 3));
        Assert.Equal("上3月", item.FormatHistory(3));
        Assert.Equal("当前", item.FormatHistory(0));
    }

    [Fact]
    public void Find_ResolvesTemplateDi()
    {
        var m = Catalog.Find(0x00010403);
        Assert.NotNull(m);
        Assert.Equal("E.FwdActive", m!.Item.Id);
        Assert.Equal(4, m.Tariff);
        Assert.Equal(3, m.History);
        Assert.Equal("正向有功电能·谷·上3月", m.Describe());

        Assert.Equal("V.Ua", Catalog.Find(0x02010100)!.Item.Id);
        Assert.Null(Catalog.Find(0x0001000D)); // 超出上 12 月
        Assert.Null(Catalog.Find(0x12345678));
    }

    [Fact]
    public void Expand_MonthsTimesAndTariffs()
    {
        var energy = Catalog.Items.Single(i => i.Id == "E.FwdActive");
        var reqs = ReadRequest.Expand(new[] { energy }, new[] { 0, 1 }, new[] { 0, 1, 2 }, 1).ToList();
        Assert.Equal(6, reqs.Count);
        Assert.Contains(reqs, r => r.Di == 0x00010102);

        var events = Catalog.Items.Single(i => i.Id == "EV.PowerDown");
        var evReqs = ReadRequest.Expand(new[] { events }, Array.Empty<int>(), Array.Empty<int>(), 3).ToList();
        Assert.Equal(new uint[] { 0x03110001, 0x03110002, 0x03110003 }, evReqs.Select(r => r.Di));
    }

    [Fact]
    public void Demand_DecodesValueAndTime()
    {
        var item = Catalog.Items.Single(i => i.Id == "D.FwdActive");
        // 需量 1.2345 kW，发生时间 2024-05-20 13:45
        var fields = DataItemDecoder.Decode(item, Hex.Parse("45 23 01 45 13 20 05 24"));
        Assert.Equal(2, fields.Count);
        Assert.Equal("1.2345", fields[0].Text);
        Assert.Equal("kW", fields[0].Unit);
        Assert.Equal("2024-05-20 13:45", fields[1].Text);
    }

    [Fact]
    public void Decoder_ExtraBytesShownAsRaw()
    {
        var item = Catalog.Items.Single(i => i.Id == "EV.LossV.A");
        var fields = DataItemDecoder.Decode(item, Hex.Parse("00 30 12 20 05 24 AA BB"));
        Assert.Equal("2024-05-20 12:30:00", fields[0].Text);
        Assert.Contains("其余数据", fields[1].Name);
        Assert.Equal("BB AA", fields[1].Text);
    }

    [Fact]
    public void ExternalJson_WithComments_Loads()
    {
        const string json = """
        // 注释
        { "categories": [ { "name": "测试", "items": [
            { "name": "测试项", "di": "02010100", "format": "XXX.X", "unit": "V" },
        ] } ] }
        """;
        var c = DataItemCatalog.LoadFromJson(json, "test");
        Assert.Single(c.Items);
        Assert.Equal(0x02010100u, c.Items[0].Di);
    }

    [Fact]
    public void ExternalJson_BadFormat_GivesReadableError()
    {
        const string json = """{ "categories": [ { "name": "测试", "items": [ { "name": "坏项", "di": "0201", "format": "XXX.X" } ] } ] }""";
        var ex = Assert.Throws<FormatException>(() => DataItemCatalog.LoadFromJson(json, "test"));
        Assert.Contains("坏项", ex.Message);
    }
}
