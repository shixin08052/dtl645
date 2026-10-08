using System.IO.Compression;
using System.Text;
using Dlt645.Core.Communication;
using Dlt645.Core.Export;

namespace Dlt645.Core.Tests;

public class ExportTests
{
    private static readonly ReadResultRow[] Rows =
    {
        new() { Index = 1, Item = "正向有功电能", Tariff = "总", Period = "当前", Di = "00010000", Value = "1234.56", Number = 1234.56, Unit = "kWh", Raw = "56341200", Status = "成功" },
        new() { Index = 2, Item = "资产管理编码", Di = "04000403", Value = "ZC,\"01\"", Status = "成功" },
    };

    [Fact]
    public void Csv_HasBomHeaderAndEscaping()
    {
        var path = Path.GetTempFileName();
        try
        {
            ResultExporter.ExportCsv(path, new ExportHeader("测试", "000012345678", new DateTime(2025, 1, 2, 3, 4, 5)), Rows);
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
            var text = Encoding.UTF8.GetString(bytes);
            Assert.Contains("000012345678", text);
            Assert.Contains("2025-01-02 03:04:05", text);
            Assert.Contains("1234.56", text);
            Assert.Contains("\"ZC,\"\"01\"\"\"", text);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Xlsx_IsValidZipWithSheet()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".xlsx");
        try
        {
            ResultExporter.ExportXlsx(path, new ExportHeader("测试", "000012345678", DateTime.Now), Rows);
            using var zip = ZipFile.OpenRead(path);
            var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
            Assert.NotNull(sheet);
            using var r = new StreamReader(sheet!.Open());
            var xml = r.ReadToEnd();
            Assert.Contains("000012345678", xml);
            Assert.Contains("<v>1234.56</v>", xml);
            Assert.Contains("&quot;01&quot;", xml);
            System.Xml.Linq.XDocument.Parse(xml); // 必须是合法 XML
        }
        finally { File.Delete(path); }
    }
}
