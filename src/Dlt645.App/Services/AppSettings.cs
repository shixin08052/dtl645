using System.IO;
using System.IO.Ports;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dlt645.Core.Communication;
using Dlt645.Core.Logging;
using Dlt645.Core.Transport;

namespace Dlt645.App.Services;

/// <summary>记住上次使用的串口与参数（settings.json）。</summary>
public sealed class AppSettings
{
    public string? PortName { get; set; }
    public int BaudRate { get; set; } = SerialSettings.DefaultBaudRate;
    public int DataBits { get; set; } = SerialSettings.DefaultDataBits;
    public Parity Parity { get; set; } = SerialSettings.DefaultParity;
    public StopBits StopBits { get; set; } = SerialSettings.DefaultStopBits;
    public int TimeoutMs { get; set; } = CommOptions.DefaultTimeoutMs;
    public int Retries { get; set; } = CommOptions.DefaultRetries;
    public bool UseSimulator { get; set; }
    public int RefreshIntervalSeconds { get; set; } = 3;
    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 860;
    public bool WindowMaximized { get; set; }
    public bool MonitorCollapsed { get; set; }
    public double MonitorHeight { get; set; } = 160;

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(AppPaths.SettingsFile))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Options) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"读取设置文件失败，使用默认设置：{ex.Message}");
        }
        return new AppSettings();
    }

    /// <summary>自检模式下不写设置文件，避免改动用户的设置。</summary>
    public static bool ReadOnly { get; set; }

    public void Save()
    {
        if (ReadOnly) return;
        try
        {
            File.WriteAllText(AppPaths.SettingsFile, JsonSerializer.Serialize(this, Options));
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"保存设置失败：{ex.Message}");
        }
    }
}
