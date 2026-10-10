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
    /// <summary>读取上 N 月数据时自动读取结算日，换算成具体日期。</summary>
    public bool AutoSettlementDate { get; set; } = true;
    public int RefreshIntervalSeconds { get; set; } = 3;
    public double WindowWidth { get; set; } = 1320;
    public double WindowHeight { get; set; } = 860;
    public bool WindowMaximized { get; set; }
    /// <summary>报文窗口是否收起；null 表示自动（窗口高度小于 800 时收起）。</summary>
    public bool? MonitorCollapsed { get; set; }
    public double MonitorHeight { get; set; } = 160;

    /// <summary>设置文件版本，用于升级时迁移旧的默认值。</summary>
    public int SettingsVersion { get; set; }

    private const int CurrentVersion = 2;

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
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(AppPaths.SettingsFile), Options) ?? new AppSettings();
                // 旧版默认波特率为 2400；电表红外口通用 1200，旧默认值自动改为 1200（用户另选的 4800/9600 保留）
                if (s.SettingsVersion < 2 && s.BaudRate == 2400)
                {
                    s.BaudRate = SerialSettings.DefaultBaudRate;
                    FileLogger.Info("设置升级：波特率由旧默认值 2400 改为电表红外口通用的 1200");
                }
                s.SettingsVersion = CurrentVersion;
                return s;
            }
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"读取设置文件失败，使用默认设置：{ex.Message}");
        }
        return new AppSettings { SettingsVersion = CurrentVersion };
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
