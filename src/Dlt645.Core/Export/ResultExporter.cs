using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using Dlt645.Core.Communication;

namespace Dlt645.Core.Export;

/// <summary>导出文件的表头信息。</summary>
public sealed record ExportHeader(string Title, string MeterNo, DateTime ReadTime, string? Extra = null);

/// <summary>
/// 把结果表导出为 CSV（UTF-8 带 BOM，Excel 直接打开不乱码）或 XLSX。
/// XLSX 为手工生成的最小 Office Open XML 包，不依赖第三方库。
/// </summary>
public static class ResultExporter
{
    private static readonly string[] Columns =
        { "序号", "数据项", "费率", "时段", "结算日期", "数值", "单位", "数据标识", "原始数据", "状态", "读取时间", "耗时(ms)" };

    private static IEnumerable<object?[]> Rows(IEnumerable<ReadResultRow> rows) =>
        rows.Select(r => new object?[]
        {
            r.Index, r.DisplayName, r.Tariff, r.Period, r.SettlementDate, (object?)r.Number ?? r.Value, r.Unit, r.Di, r.Raw, r.Status,
            r.TimeText, r.ElapsedMs,
        });

    private static IEnumerable<string[]> HeaderLines(ExportHeader h)
    {
        yield return new[] { h.Title };
        yield return new[] { "表号", h.MeterNo };
        yield return new[] { "读取时间", h.ReadTime.ToString("yyyy-MM-dd HH:mm:ss") };
        if (!string.IsNullOrEmpty(h.Extra)) yield return new[] { "备注", h.Extra };
    }

    // ---------------------------------------------------------------- CSV

    public static void ExportCsv(string path, ExportHeader header, IEnumerable<ReadResultRow> rows)
    {
        var sb = new StringBuilder();
        foreach (var line in HeaderLines(header)) sb.AppendLine(string.Join(",", line.Select(Csv)));
        sb.AppendLine();
        sb.AppendLine(string.Join(",", Columns.Select(Csv)));
        foreach (var row in Rows(rows))
            sb.AppendLine(string.Join(",", row.Select(v => Csv(FormatValue(v)))));
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string FormatValue(object? v) => v switch
    {
        null => string.Empty,
        double d => d.ToString("0.########", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? string.Empty,
    };

    private static string Csv(string s)
    {
        // 以 0 开头的长数字串（表号、原始数据）加制表符前缀，防止 Excel 去掉前导 0 或转成科学计数
        bool needsText = s.Length > 1 && s.All(char.IsAsciiHexDigit) && (s[0] == '0' || s.Length >= 11);
        if (needsText) s = "\t" + s;
        if (s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0) s = "\"" + s.Replace("\"", "\"\"") + "\"";
        return s;
    }

    // ---------------------------------------------------------------- XLSX

    public static void ExportXlsx(string path, ExportHeader header, IEnumerable<ReadResultRow> rows)
    {
        var sheet = new StringBuilder();
        sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
        sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
        sheet.Append("<cols>");
        int[] widths = { 6, 34, 6, 12, 14, 18, 8, 11, 26, 22, 20, 9 };
        for (int i = 0; i < widths.Length; i++)
            sheet.Append($"<col min=\"{i + 1}\" max=\"{i + 1}\" width=\"{widths[i]}\" customWidth=\"1\"/>");
        sheet.Append("</cols><sheetData>");

        int r = 1;
        foreach (var line in HeaderLines(header))
        {
            AppendRow(sheet, r, line.Cast<object?>().ToArray(), bold: r == 1);
            r++;
        }
        r++;
        AppendRow(sheet, r++, Columns.Cast<object?>().ToArray(), bold: true);
        foreach (var row in Rows(rows)) AppendRow(sheet, r++, row, bold: false);
        sheet.Append("</sheetData></worksheet>");

        if (File.Exists(path)) File.Delete(path);
        using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
            "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
            "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
            "<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>" +
            "</Types>");
        Add(zip, "_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
            "</Relationships>");
        Add(zip, "xl/workbook.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
            "<sheets><sheet name=\"读取结果\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Add(zip, "xl/_rels/workbook.xml.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
            "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>" +
            "</Relationships>");
        Add(zip, "xl/styles.xml",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<fonts count=\"2\"><font><sz val=\"11\"/><name val=\"宋体\"/></font><font><b/><sz val=\"11\"/><name val=\"宋体\"/></font></fonts>" +
            "<fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills>" +
            "<borders count=\"1\"><border><left/><right/><top/><bottom/><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"2\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/></cellXfs>" +
            "</styleSheet>");
        Add(zip, "xl/worksheets/sheet1.xml", sheet.ToString());
    }

    private static void AppendRow(StringBuilder sb, int rowIndex, object?[] values, bool bold)
    {
        sb.Append($"<row r=\"{rowIndex}\">");
        for (int c = 0; c < values.Length; c++)
        {
            string cellRef = ColumnName(c) + rowIndex.ToString(CultureInfo.InvariantCulture);
            string style = bold ? " s=\"1\"" : string.Empty;
            switch (values[c])
            {
                case null:
                    break;
                case double d:
                    sb.Append($"<c r=\"{cellRef}\"{style}><v>{d.ToString("R", CultureInfo.InvariantCulture)}</v></c>");
                    break;
                case int or long:
                    sb.Append($"<c r=\"{cellRef}\"{style}><v>{Convert.ToString(values[c], CultureInfo.InvariantCulture)}</v></c>");
                    break;
                default:
                    var text = SecurityElement.Escape(values[c]!.ToString() ?? string.Empty);
                    sb.Append($"<c r=\"{cellRef}\" t=\"inlineStr\"{style}><is><t xml:space=\"preserve\">{text}</t></is></c>");
                    break;
            }
        }
        sb.Append("</row>");
    }

    private static string ColumnName(int index)
    {
        var name = string.Empty;
        index++;
        while (index > 0)
        {
            int m = (index - 1) % 26;
            name = (char)('A' + m) + name;
            index = (index - 1) / 26;
        }
        return name;
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }
}
