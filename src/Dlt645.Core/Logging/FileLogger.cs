using System.Text;

namespace Dlt645.Core.Logging;

/// <summary>
/// 简单的按日期分文件日志：{目录}/logs/yyyy-MM-dd.log。
/// 线程安全；写入失败不会影响程序运行。同时在内存中保留最近的错误，供“导出诊断信息”使用。
/// </summary>
public static class FileLogger
{
    private static readonly object Gate = new();
    private static readonly LinkedList<string> RecentErrorList = new();
    private const int MaxRecentErrors = 300;

    public static string LogDirectory { get; private set; } = Path.Combine(AppContext.BaseDirectory, "logs");

    /// <summary>最近一次写入失败的原因（用于界面提示）。</summary>
    public static string? LastWriteError { get; private set; }

    public static void Initialize(string baseDirectory)
    {
        LogDirectory = Path.Combine(baseDirectory, "logs");
        try { Directory.CreateDirectory(LogDirectory); }
        catch (Exception ex) { LastWriteError = ex.Message; }
    }

    public static string CurrentFile => Path.Combine(LogDirectory, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

    public static void Info(string message) => Write("信息", message);
    public static void Warn(string message) => Write("警告", message);
    public static void Frame(string message) => Write("报文", message);

    public static void Error(string message, Exception? ex = null)
    {
        var text = ex is null ? message : $"{message}{Environment.NewLine}{ex}";
        lock (Gate)
        {
            RecentErrorList.AddLast($"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {text}");
            while (RecentErrorList.Count > MaxRecentErrors) RecentErrorList.RemoveFirst();
        }
        Write("错误", text);
    }

    public static IReadOnlyList<string> RecentErrors
    {
        get { lock (Gate) return RecentErrorList.ToList(); }
    }

    private static void Write(string level, string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(CurrentFile, line, Encoding.UTF8);
                LastWriteError = null;
            }
            catch (Exception ex)
            {
                LastWriteError = ex.Message;
            }
        }
    }
}
