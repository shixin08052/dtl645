using System.IO;
using System.Reflection;

namespace Dlt645.App.Services;

/// <summary>
/// 程序路径。日志、设置默认写在程序（exe）所在目录；
/// 若该目录不可写（例如放在 C:\Program Files 下），自动改用 %LocalAppData%\Dlt645Reader。
/// </summary>
public static class AppPaths
{
    public static string ProgramDirectory { get; } = AppContext.BaseDirectory;

    public static string DataDirectory { get; private set; } = AppContext.BaseDirectory;

    public static bool UsingFallbackDirectory { get; private set; }

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0]
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "1.0.0";

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");

    public static void Initialize()
    {
        if (IsWritable(ProgramDirectory))
        {
            DataDirectory = ProgramDirectory;
            return;
        }
        DataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Dlt645Reader");
        Directory.CreateDirectory(DataDirectory);
        UsingFallbackDirectory = true;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write_test_{Environment.ProcessId}.tmp");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
