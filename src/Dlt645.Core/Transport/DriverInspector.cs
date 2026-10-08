using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace Dlt645.Core.Transport;

/// <summary>驱动检测的总体结论。</summary>
public enum DriverHealth
{
    /// <summary>CH340 设备已插入且驱动正常。</summary>
    Ok,
    /// <summary>CH340 设备已插入，但驱动未安装或工作异常。</summary>
    DeviceProblem,
    /// <summary>未插入 CH340 设备。</summary>
    NoDevice,
    /// <summary>非 Windows 系统，无法检测。</summary>
    NotSupported,
}

/// <summary>一个已插入的 WCH（沁恒）USB 串口设备。</summary>
public sealed record UsbSerialDevice(
    string InstanceId,
    string Name,
    string HardwareId,
    string? PortName,
    uint ProblemCode,
    string? Service,
    string? DriverVersion,
    string? DriverDate,
    string? DriverProvider)
{
    public bool HasProblem => ProblemCode != 0;

    public string Chip => DriverInspector.ChipName(HardwareId);

    public string StatusText => HasProblem ? DriverInspector.DescribeProblem(ProblemCode) : "正常";

    public string DriverText =>
        DriverVersion is null ? "（无驱动）" : $"{DriverVersion}" + (DriverDate is null ? "" : $"（{DriverDate}）") + (DriverProvider is null ? "" : $" {DriverProvider}");
}

/// <summary>驱动检测报告。</summary>
public sealed record DriverReport(
    DriverHealth Health,
    IReadOnlyList<UsbSerialDevice> Devices,
    string? InstalledDriverFile,
    string? InstalledDriverVersion,
    IReadOnlyList<string> DriverPackages,
    string Summary)
{
    /// <summary>系统中已有 CH340 驱动（驱动文件或驱动包存在）。</summary>
    public bool DriverInstalled => InstalledDriverFile != null || DriverPackages.Count > 0;

    public UsbSerialDevice? ProblemDevice => Devices.FirstOrDefault(d => d.HasProblem);
}

/// <summary>
/// CH340 驱动检测：用 SetupAPI 枚举当前插入的 USB 设备（VID 1A86），读取设备管理器中的错误代码；
/// 同时检查系统中是否已有 CH341SER 驱动。全部为只读操作，不需要管理员权限。
/// </summary>
public static class DriverInspector
{
    public const string WchVendorId = "1A86";

    /// <summary>沁恒官方 CH340/CH341 驱动下载页。</summary>
    public const string OfficialDownloadPage = "https://www.wch.cn/downloads/CH341SER_EXE.html";

    public static DriverReport Inspect()
    {
        if (!OperatingSystem.IsWindows())
            return new DriverReport(DriverHealth.NotSupported, Array.Empty<UsbSerialDevice>(), null, null, Array.Empty<string>(), "当前系统不是 Windows，无法检测驱动");

        List<UsbSerialDevice> devices;
        try { devices = EnumerateWchDevices(); }
        catch { devices = new List<UsbSerialDevice>(); }

        var (file, version) = FindDriverFile();
        var packages = FindDriverPackages();
        return BuildReport(devices, file, version, packages);
    }

    /// <summary>根据检测结果生成结论（纯逻辑，便于单元测试）。</summary>
    public static DriverReport BuildReport(IReadOnlyList<UsbSerialDevice> devices, string? driverFile, string? driverVersion, IReadOnlyList<string> packages)
    {
        bool installed = driverFile != null || packages.Count > 0;
        string installedText = driverVersion != null ? $"驱动版本 {driverVersion}" : installed ? "驱动已安装" : "未发现 CH340 驱动";

        var problem = devices.FirstOrDefault(d => d.HasProblem);
        if (problem != null)
        {
            return new DriverReport(DriverHealth.DeviceProblem, devices, driverFile, driverVersion, packages,
                $"检测到 {problem.Chip} 设备，但{DescribeProblem(problem.ProblemCode)}。请点击“安装驱动”，安装后重新插拔红外通讯头。");
        }

        var ok = devices.FirstOrDefault();
        if (ok != null)
        {
            var ports = string.Join("、", devices.Where(d => d.PortName != null).Select(d => d.PortName));
            return new DriverReport(DriverHealth.Ok, devices, driverFile, driverVersion, packages,
                $"{ok.Chip} 工作正常" + (ports.Length > 0 ? $"，串口 {ports}" : "") + (ok.DriverVersion != null ? $"，驱动版本 {ok.DriverVersion}" : "") + "。");
        }

        return new DriverReport(DriverHealth.NoDevice, devices, driverFile, driverVersion, packages,
            installed
                ? $"未插入 CH340 设备（{installedText}）。请插入红外通讯头；插入后若串口列表仍无【CH340】，请点“重新检测”。"
                : "未插入 CH340 设备，系统中也未发现 CH340 驱动。Windows 10/11 联网插入设备时通常会自动安装；离线电脑请点击“安装驱动”。");
    }

    /// <summary>设备管理器错误代码的中文说明。</summary>
    public static string DescribeProblem(uint code) => code switch
    {
        0 => "正常",
        1 => "设备配置不正确（错误代码 1），需要重新安装驱动",
        3 => "驱动可能已损坏或内存不足（错误代码 3）",
        10 => "设备无法启动（错误代码 10），通常是驱动版本不匹配，请重新安装驱动",
        18 => "需要重新安装驱动（错误代码 18）",
        19 => "注册表中的配置信息损坏（错误代码 19），请重新安装驱动",
        21 => "Windows 正在删除此设备（错误代码 21），请重新插拔",
        22 => "设备已被禁用（错误代码 22），请在设备管理器中右键“启用设备”",
        24 => "设备不存在或未正常工作（错误代码 24），请重新插拔",
        28 => "驱动程序未安装（错误代码 28）",
        31 => "驱动无法加载（错误代码 31），请重新安装驱动",
        37 => "驱动初始化失败（错误代码 37），请重新安装驱动",
        39 => "驱动可能已损坏或丢失（错误代码 39），请重新安装驱动",
        43 => "设备报告了问题（错误代码 43），请更换 USB 口或重新插拔",
        45 => "设备当前未连接（错误代码 45）",
        48 => "驱动被系统阻止（错误代码 48），请安装新版驱动",
        52 => "驱动数字签名无法验证（错误代码 52），请从沁恒官网下载最新驱动",
        _ => $"设备工作异常（错误代码 {code}）",
    };

    /// <summary>按 PID 识别芯片型号。</summary>
    public static string ChipName(string hardwareId)
    {
        var id = hardwareId.ToUpperInvariant();
        if (id.Contains("PID_7523")) return "CH340";
        if (id.Contains("PID_7522")) return "CH340K";
        if (id.Contains("PID_5523")) return "CH341";
        if (id.Contains("PID_55D4")) return "CH9102";
        if (id.Contains("PID_55D3")) return "CH343";
        var i = id.IndexOf("PID_", StringComparison.Ordinal);
        return i >= 0 && id.Length >= i + 8 ? $"沁恒 USB 串口（PID {id.Substring(i + 4, 4)}）" : "沁恒 USB 串口";
    }

    // ================================================================ 系统中的驱动

    private static (string? File, string? Version) FindDriverFile()
    {
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers");
            foreach (var name in new[] { "CH341S64.SYS", "CH341SER.SYS" })
            {
                var path = Path.Combine(dir, name);
                if (!File.Exists(path)) continue;
                var v = FileVersionInfo.GetVersionInfo(path);
                return (path, string.IsNullOrWhiteSpace(v.FileVersion) ? null : v.FileVersion.Trim());
            }
        }
        catch { }
        return (null, null);
    }

    private static IReadOnlyList<string> FindDriverPackages()
    {
        try
        {
            var repo = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "DriverStore", "FileRepository");
            if (!Directory.Exists(repo)) return Array.Empty<string>();
            return Directory.GetDirectories(repo, "ch341ser*").Select(Path.GetFileName).OfType<string>().ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ================================================================ SetupAPI

    [SupportedOSPlatform("windows")]
    private static List<UsbSerialDevice> EnumerateWchDevices()
    {
        var result = new List<UsbSerialDevice>();
        IntPtr set = SetupDiGetClassDevsW(IntPtr.Zero, "USB", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
        if (set == InvalidHandle) return result;
        try
        {
            var data = new SpDevinfoData { cbSize = (uint)Marshal.SizeOf<SpDevinfoData>() };
            for (uint i = 0; SetupDiEnumDeviceInfo(set, i, ref data); i++)
            {
                var hwIds = GetStringProperty(set, ref data, SpdrpHardwareId);
                var hwId = hwIds?.Split('\0', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
                if (!hwId.Contains("VID_" + WchVendorId, StringComparison.OrdinalIgnoreCase)) continue;
                // 复合设备的父节点（无 MI_/有接口）也可能匹配，这里保留所有 VID_1A86 的节点
                string instanceId = GetInstanceId(set, ref data) ?? hwId;
                string name = GetStringProperty(set, ref data, SpdrpFriendlyName)
                              ?? GetStringProperty(set, ref data, SpdrpDeviceDesc)
                              ?? "USB 设备";
                string? service = GetStringProperty(set, ref data, SpdrpService);
                string? driverKey = GetStringProperty(set, ref data, SpdrpDriver);

                uint problem = 0;
                if (CM_Get_DevNode_Status(out uint status, out uint prob, data.DevInst, 0) == 0 && (status & DnHasProblem) != 0)
                    problem = prob;
                // 没有驱动但系统未报告错误代码时，按“未安装驱动”处理
                if (problem == 0 && string.IsNullOrEmpty(driverKey) && string.IsNullOrEmpty(service)) problem = 28;

                string? port = ReadPortName(instanceId);
                var (ver, date, provider) = ReadDriverInfo(driverKey);
                result.Add(new UsbSerialDevice(instanceId, name.TrimEnd('\0'), hwId, port, problem, service, ver, date, provider));
            }
        }
        finally
        {
            SetupDiDestroyDeviceInfoList(set);
        }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static string? ReadPortName(string instanceId)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");
            return key?.GetValue("PortName") as string;
        }
        catch
        {
            return null;
        }
    }

    [SupportedOSPlatform("windows")]
    private static (string? Version, string? Date, string? Provider) ReadDriverInfo(string? driverKey)
    {
        if (string.IsNullOrEmpty(driverKey)) return (null, null, null);
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Control\Class\{driverKey}");
            if (key is null) return (null, null, null);
            return (key.GetValue("DriverVersion") as string, key.GetValue("DriverDate") as string, key.GetValue("ProviderName") as string);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? GetStringProperty(IntPtr set, ref SpDevinfoData data, uint property)
    {
        SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, null, 0, out uint required);
        if (required == 0) return null;
        var buffer = new byte[required];
        if (!SetupDiGetDeviceRegistryPropertyW(set, ref data, property, out _, buffer, required, out _)) return null;
        var s = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
        return s.Length == 0 ? null : s;
    }

    private static string? GetInstanceId(IntPtr set, ref SpDevinfoData data)
    {
        var buf = new char[512];
        return SetupDiGetDeviceInstanceIdW(set, ref data, buf, buf.Length, out int required)
            ? new string(buf, 0, Math.Max(0, required - 1)).TrimEnd('\0')
            : null;
    }

    private const uint DigcfPresent = 0x2;
    private const uint DigcfAllClasses = 0x4;
    private const uint SpdrpDeviceDesc = 0x0;
    private const uint SpdrpHardwareId = 0x1;
    private const uint SpdrpService = 0x4;
    private const uint SpdrpDriver = 0x9;
    private const uint SpdrpFriendlyName = 0xC;
    private const uint DnHasProblem = 0x400;
    private static readonly IntPtr InvalidHandle = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct SpDevinfoData
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevsW(IntPtr classGuid, string? enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(IntPtr deviceInfoSet, uint memberIndex, ref SpDevinfoData deviceInfoData);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceRegistryPropertyW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, uint property,
        out uint propertyRegDataType, [Out] byte[]? propertyBuffer, uint propertyBufferSize, out uint requiredSize);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool SetupDiGetDeviceInstanceIdW(IntPtr deviceInfoSet, ref SpDevinfoData deviceInfoData, [Out] char[] deviceInstanceId,
        int deviceInstanceIdSize, out int requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("cfgmgr32.dll")]
    private static extern int CM_Get_DevNode_Status(out uint status, out uint problemNumber, uint devInst, uint flags);
}
