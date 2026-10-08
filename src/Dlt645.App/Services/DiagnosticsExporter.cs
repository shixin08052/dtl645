using System.IO;
using System.IO.Compression;
using System.Text;
using Dlt645.Core.Logging;

namespace Dlt645.App.Services;

/// <summary>“导出诊断信息”：把版本、参数、最近报文、错误和日志打包成 zip。</summary>
public static class DiagnosticsExporter
{
    public static void Export(string zipPath, string summary, IEnumerable<string> recentFrames)
    {
        if (File.Exists(zipPath)) File.Delete(zipPath);
        using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);

        AddText(zip, "诊断信息.txt", summary);
        AddText(zip, "最近报文.txt", string.Join(Environment.NewLine, recentFrames));

        var errors = FileLogger.RecentErrors;
        AddText(zip, "最近错误.txt", errors.Count == 0 ? "（本次运行没有错误记录）" : string.Join(Environment.NewLine, errors));

        // 最近 3 天的日志文件
        try
        {
            var dir = new DirectoryInfo(FileLogger.LogDirectory);
            if (dir.Exists)
            {
                foreach (var file in dir.GetFiles("*.log").OrderByDescending(f => f.Name).Take(3))
                {
                    using var src = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var entry = zip.CreateEntry("logs/" + file.Name, CompressionLevel.Optimal);
                    using var dst = entry.Open();
                    src.CopyTo(dst);
                }
            }
        }
        catch (Exception ex)
        {
            AddText(zip, "logs/读取日志失败.txt", ex.ToString());
        }
    }

    private static void AddText(ZipArchive zip, string name, string text)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(true));
        w.Write(text);
    }
}
