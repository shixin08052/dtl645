using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;

namespace Dlt645.App.Services;

/// <summary>驱动安装结果。</summary>
public sealed record DriverInstallResult(bool Completed, bool Cancelled, bool RebootRequired, string Message);

/// <summary>
/// CH340 驱动安装：支持沁恒官方安装程序（CH341SER.EXE / SETUP.EXE）、官方压缩包（CH341SER.ZIP）
/// 以及解压后的 INF（通过系统自带 pnputil 安装）。安装驱动需要管理员权限，会弹出系统 UAC 提示；
/// 本程序本身仍以普通权限运行。
/// </summary>
public static class DriverInstaller
{
    public const string FileFilter = "CH340 驱动安装包 (*.exe;*.zip;*.inf)|*.exe;*.zip;*.inf|所有文件 (*.*)|*.*";

    /// <summary>在程序目录、drivers、驱动 子文件夹中查找驱动安装包。</summary>
    public static string? FindLocalInstaller()
    {
        var dirs = new[]
        {
            AppPaths.ProgramDirectory,
            Path.Combine(AppPaths.ProgramDirectory, "drivers"),
            Path.Combine(AppPaths.ProgramDirectory, "driver"),
            Path.Combine(AppPaths.ProgramDirectory, "驱动"),
        };
        foreach (var pattern in new[] { "CH341SER*.EXE", "CH341SER*.ZIP", "CH341SER*.INF" })
        {
            foreach (var dir in dirs)
            {
                try
                {
                    if (!Directory.Exists(dir)) continue;
                    var hit = Directory.GetFiles(dir, pattern).OrderBy(f => f, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
                    if (hit != null) return hit;
                }
                catch
                {
                    // 无权限的目录忽略
                }
            }
        }
        return null;
    }

    public static async Task<DriverInstallResult> InstallAsync(string path, Action<string> log)
    {
        if (!File.Exists(path)) return new DriverInstallResult(false, false, false, $"文件不存在：{path}");
        var ext = Path.GetExtension(path).ToLowerInvariant();
        try
        {
            return ext switch
            {
                ".zip" => await InstallFromZipAsync(path, log),
                ".inf" => await InstallInfAsync(path, log),
                ".exe" => await RunInstallerAsync(path, log),
                _ => new DriverInstallResult(false, false, false, "不支持的文件类型，请选择 CH341SER.EXE、CH341SER.ZIP 或 .INF 文件"),
            };
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new DriverInstallResult(false, true, false, "已取消：在系统提示中选择了“否”。安装驱动需要管理员权限。");
        }
        catch (Exception ex)
        {
            return new DriverInstallResult(false, false, false, ex.Message);
        }
    }

    private static async Task<DriverInstallResult> RunInstallerAsync(string exe, Action<string> log)
    {
        log($"启动驱动安装程序：{exe}（请在弹出的窗口中点击“安装/INSTALL”）");
        using var p = Process.Start(new ProcessStartInfo(exe)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(exe) ?? AppPaths.ProgramDirectory,
        }) ?? throw new InvalidOperationException("无法启动安装程序");
        await p.WaitForExitAsync();
        log($"驱动安装程序已关闭（退出码 {p.ExitCode}）");
        return new DriverInstallResult(true, false, false, "安装程序已运行完毕");
    }

    private static async Task<DriverInstallResult> InstallInfAsync(string inf, Action<string> log)
    {
        log($"使用系统 pnputil 安装驱动：{inf}");
        using var p = Process.Start(new ProcessStartInfo("pnputil.exe", $"/add-driver \"{inf}\" /install")
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden,
        }) ?? throw new InvalidOperationException("无法启动 pnputil");
        await p.WaitForExitAsync();
        log($"pnputil 退出码 {p.ExitCode}");
        return p.ExitCode switch
        {
            0 => new DriverInstallResult(true, false, false, "驱动安装成功"),
            3010 => new DriverInstallResult(true, false, true, "驱动安装成功，需要重启电脑后生效"),
            259 => new DriverInstallResult(true, false, false, "驱动已添加到系统（当前没有需要更新的设备）"),
            _ => new DriverInstallResult(false, false, false, $"pnputil 安装失败（退出码 {p.ExitCode}）"),
        };
    }

    private static async Task<DriverInstallResult> InstallFromZipAsync(string zip, Action<string> log)
    {
        var dir = Path.Combine(Path.GetTempPath(), "Dlt645Reader-driver-" + DateTime.Now.ToString("yyyyMMddHHmmss"));
        log($"解压驱动包到 {dir}");
        ZipFile.ExtractToDirectory(zip, dir, overwriteFiles: true);
        var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
        var exe = files.FirstOrDefault(f => Path.GetFileName(f).Equals("SETUP.EXE", StringComparison.OrdinalIgnoreCase))
                  ?? files.FirstOrDefault(f => Path.GetFileName(f).StartsWith("CH341SER", StringComparison.OrdinalIgnoreCase)
                                               && f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (exe != null) return await RunInstallerAsync(exe, log);
        var inf = files.FirstOrDefault(f => f.EndsWith(".inf", StringComparison.OrdinalIgnoreCase)
                                            && Path.GetFileName(f).Contains("CH341", StringComparison.OrdinalIgnoreCase))
                  ?? files.FirstOrDefault(f => f.EndsWith(".inf", StringComparison.OrdinalIgnoreCase));
        if (inf != null) return await InstallInfAsync(inf, log);
        return new DriverInstallResult(false, false, false, "压缩包中没有找到 SETUP.EXE 或 .INF 驱动文件");
    }

    public static void OpenDeviceManager() =>
        Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });

    public static void OpenUrl(string url) =>
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static void OpenFolder(string dir) =>
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
}
