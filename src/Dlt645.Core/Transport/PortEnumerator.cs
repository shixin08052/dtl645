using System.IO.Ports;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Dlt645.Core.Transport;

/// <summary>串口信息。</summary>
public sealed record PortInfo(string PortName, string Description, bool IsCh340)
{
    /// <summary>下拉框中显示的文字。</summary>
    public string Display => IsCh340
        ? $"{PortName}  【CH340】 {Description}"
        : string.IsNullOrEmpty(Description) ? PortName : $"{PortName}  {Description}";

    public override string ToString() => Display;
}

/// <summary>
/// 枚举串口并识别 CH340。
/// 通过注册表读取设备友好名称与 USB VID/PID（无需管理员权限，也不依赖 WMI）。
/// CH340/CH341 的 USB VID 为 1A86，驱动创建的设备名形如 \Device\wchusbserialN。
/// </summary>
public static partial class PortEnumerator
{
    public const string WchVendorId = "1A86";

    public static IReadOnlyList<PortInfo> Enumerate()
    {
        string[] names;
        try { names = SerialPort.GetPortNames(); }
        catch { names = Array.Empty<string>(); }

        var details = OperatingSystem.IsWindows() ? ReadWindowsDetails() : new Dictionary<string, (string, bool)>();

        return names
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(PortNumber)
            .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => details.TryGetValue(n, out var d)
                ? new PortInfo(n, d.Item1, d.Item2)
                : new PortInfo(n, string.Empty, false))
            .ToList();
    }

    private static int PortNumber(string name)
    {
        var m = DigitsRegex().Match(name);
        return m.Success && int.TryParse(m.Value, out var n) ? n : int.MaxValue;
    }

    [GeneratedRegex(@"\d+$")]
    private static partial Regex DigitsRegex();

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static Dictionary<string, (string Description, bool IsCh340)> ReadWindowsDetails()
    {
        var result = new Dictionary<string, (string, bool)>(StringComparer.OrdinalIgnoreCase);

        // 1) 当前存在的串口：设备路径 → COM 名称
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
            if (key != null)
            {
                foreach (var valueName in key.GetValueNames())
                {
                    if (key.GetValue(valueName) is not string com) continue;
                    bool wch = valueName.Contains("wchusbserial", StringComparison.OrdinalIgnoreCase);
                    result[com] = (string.Empty, wch);
                }
            }
        }
        catch { /* 无权限时忽略 */ }

        // 2) USB 设备的友好名称与 VID。注册表会残留已拔出设备的记录，
        //    当前在线的设备实例下才有易失性的 Control 子键，据此优先选取在线记录。
        var candidates = new List<(string Com, string Friendly, bool Ch340, bool Present)>();
        try
        {
            using var usb = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\USB");
            if (usb != null)
            {
                foreach (var devId in usb.GetSubKeyNames())
                {
                    bool wchVid = devId.Contains("VID_" + WchVendorId, StringComparison.OrdinalIgnoreCase);
                    try
                    {
                        using var dev = usb.OpenSubKey(devId);
                        if (dev == null) continue;
                        foreach (var inst in dev.GetSubKeyNames())
                        {
                            try
                            {
                                using var instKey = dev.OpenSubKey(inst);
                                using var param = instKey?.OpenSubKey("Device Parameters");
                                if (param?.GetValue("PortName") is not string com) continue;
                                var friendly = instKey!.GetValue("FriendlyName") as string ?? string.Empty;
                                friendly = Regex.Replace(friendly, @"\s*\(COM\d+\)\s*$", "");
                                bool ch340 = wchVid || friendly.Contains("CH34", StringComparison.OrdinalIgnoreCase);
                                using var control = instKey.OpenSubKey("Control");
                                candidates.Add((com, friendly, ch340, control != null));
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
        }
        catch { }

        foreach (var group in candidates.GroupBy(c => c.Com, StringComparer.OrdinalIgnoreCase))
        {
            var present = group.Where(c => c.Present).ToList();
            bool known = result.TryGetValue(group.Key, out var fromMap);
            if (present.Count > 0)
            {
                var c = present[0];
                result[group.Key] = (c.Friendly, c.Ch340 || (known && fromMap.Item2));
            }
            else if (!known)
            {
                // SERIALCOMM 中没有该端口的信息时，才退而使用历史记录
                var c = group.Last();
                result[group.Key] = (c.Friendly, c.Ch340);
            }
        }

        return result;
    }
}
