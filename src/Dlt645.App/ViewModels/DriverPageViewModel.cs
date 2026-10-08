using System.IO;
using System.Windows;
using System.Windows.Input;
using Dlt645.App.Infrastructure;
using Dlt645.App.Services;
using Dlt645.Core.Errors;
using Dlt645.Core.Logging;
using Dlt645.Core.Transport;

namespace Dlt645.App.ViewModels;

/// <summary>“驱动检测”页：检测 CH340 设备与驱动状态，一键安装驱动。</summary>
public sealed class DriverPageViewModel : PageViewModel
{
    private DriverReport _report;
    private string? _localInstaller;
    private bool _isInstalling;
    private string _lastCheckText = string.Empty;

    public DriverPageViewModel(MainViewModel main)
        : base(main, "驱动检测", "",
            "检测红外通讯头（CH340/CH341 芯片）是否插好、驱动是否正常，并可一键安装驱动。检测不需要管理员权限；安装驱动时系统会弹出权限确认（仅首次安装需要）。")
    {
        _report = new DriverReport(DriverHealth.NoDevice, Array.Empty<UsbSerialDevice>(), null, null, Array.Empty<string>(), "尚未检测");
        RefreshCommand = new RelayCommand(() =>
        {
            Main.RefreshPorts(userInitiated: true);
            Main.AddInfo("驱动检测：" + Report.Summary);
        }, () => !IsInstalling);
        InstallCommand = new AsyncRelayCommand(InstallAutoAsync, () => !IsInstalling && !Main.IsBusy);
        InstallFromFileCommand = new AsyncRelayCommand(InstallFromFileAsync, () => !IsInstalling && !Main.IsBusy);
        OpenDeviceManagerCommand = new RelayCommand(() => Try(DriverInstaller.OpenDeviceManager, "打开设备管理器"));
        OpenDownloadPageCommand = new RelayCommand(() => Try(() => DriverInstaller.OpenUrl(DriverInspector.OfficialDownloadPage), "打开官网下载页"));
        OpenProgramFolderCommand = new RelayCommand(() => Try(() => DriverInstaller.OpenFolder(AppPaths.ProgramDirectory), "打开程序文件夹"));
    }

    public override ICommand ReadCommand => RefreshCommand;

    public DriverReport Report
    {
        get => _report;
        private set
        {
            _report = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(Health));
            OnPropertyChanged(nameof(HealthTitle));
            OnPropertyChanged(nameof(Devices));
            OnPropertyChanged(nameof(HasDevices));
            OnPropertyChanged(nameof(DriverFileText));
        }
    }

    public DriverHealth Health => Report.Health;

    public string HealthTitle => Report.Health switch
    {
        DriverHealth.Ok => "✔ CH340 驱动正常",
        DriverHealth.DeviceProblem => "✖ CH340 驱动未安装或异常",
        DriverHealth.NotSupported => "无法检测",
        _ => Report.DriverInstalled ? "● 未插入红外通讯头（驱动已安装）" : "● 未插入红外通讯头",
    };

    public IReadOnlyList<UsbSerialDevice> Devices => Report.Devices;

    public bool HasDevices => Report.Devices.Count > 0;

    public string DriverFileText
    {
        get
        {
            var parts = new List<string>();
            parts.Add(Report.InstalledDriverFile is null
                ? "驱动文件：未找到 CH341S64.SYS / CH341SER.SYS"
                : $"驱动文件：{Report.InstalledDriverFile}" + (Report.InstalledDriverVersion is null ? "" : $"（版本 {Report.InstalledDriverVersion}）"));
            parts.Add(Report.DriverPackages.Count == 0
                ? "驱动包：系统驱动库中没有 ch341ser.inf"
                : $"驱动包：{string.Join("、", Report.DriverPackages)}");
            return string.Join(Environment.NewLine, parts);
        }
    }

    public string LocalInstallerText => _localInstaller is null
        ? "程序文件夹中没有驱动安装包。离线电脑可先把沁恒官网下载的 CH341SER.EXE 放到本程序同一文件夹（或 drivers 子文件夹），即可一键安装。"
        : $"已找到驱动安装包：{_localInstaller}";

    public bool HasLocalInstaller => _localInstaller != null;

    public bool IsInstalling
    {
        get => _isInstalling;
        private set
        {
            if (!SetProperty(ref _isInstalling, value)) return;
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public string LastCheckText
    {
        get => _lastCheckText;
        private set => SetProperty(ref _lastCheckText, value);
    }

    public ICommand RefreshCommand { get; }
    public ICommand InstallCommand { get; }
    public ICommand InstallFromFileCommand { get; }
    public ICommand OpenDeviceManagerCommand { get; }
    public ICommand OpenDownloadPageCommand { get; }
    public ICommand OpenProgramFolderCommand { get; }

    /// <summary>重新检测（串口列表刷新时由主界面调用）。</summary>
    public DriverReport Refresh()
    {
        try
        {
            Report = DriverInspector.Inspect();
        }
        catch (Exception ex)
        {
            FileLogger.Error("驱动检测失败", ex);
        }
        _localInstaller = DriverInstaller.FindLocalInstaller();
        OnPropertyChanged(nameof(LocalInstallerText));
        OnPropertyChanged(nameof(HasLocalInstaller));
        LastCheckText = $"检测时间 {DateTime.Now:HH:mm:ss}";
        return Report;
    }

    private async Task InstallAutoAsync()
    {
        Refresh();
        if (_localInstaller != null)
        {
            if (!Dialogs.Confirm($"将使用以下安装包安装 CH340 驱动：\n{_localInstaller}\n\n系统会弹出“是否允许更改”的提示，请选择“是”。是否继续？")) return;
            await InstallAsync(_localInstaller);
            return;
        }

        var answer = Dialogs.Ask(
            "程序文件夹中没有找到 CH340 驱动安装包。\n\n" +
            "【是】选择已下载的驱动文件（CH341SER.EXE / .ZIP / .INF）\n" +
            "【否】打开沁恒官网下载页（需要联网），下载 CH341SER.EXE 后再点“安装驱动”\n" +
            "【取消】暂不安装",
            "安装 CH340 驱动");
        if (answer == MessageBoxResult.Yes) await InstallFromFileAsync();
        else if (answer == MessageBoxResult.No) Try(() => DriverInstaller.OpenUrl(DriverInspector.OfficialDownloadPage), "打开官网下载页");
    }

    private async Task InstallFromFileAsync()
    {
        var path = Dialogs.OpenFile("选择 CH340 驱动安装包", DriverInstaller.FileFilter);
        if (path is null) return;
        await InstallAsync(path);
    }

    private async Task InstallAsync(string path)
    {
        IsInstalling = true;
        try
        {
            Main.AddInfo($"开始安装 CH340 驱动：{path}");
            var result = await DriverInstaller.InstallAsync(path, Main.AddInfo);
            if (result.Cancelled)
            {
                Main.ShowError(new MeterException(ErrorCode.DriverInstallFailed, result.Message), warning: true);
                return;
            }

            // 稍等系统完成驱动绑定，再重新检测
            await Task.Delay(1500);
            Main.RefreshPorts(userInitiated: true);
            var report = Report;
            Main.AddInfo("安装后检测：" + report.Summary);

            if (!result.Completed)
            {
                Main.ShowError(new MeterException(ErrorCode.DriverInstallFailed, result.Message));
            }
            else if (report.Health == DriverHealth.Ok)
            {
                Dialogs.Info("CH340 驱动工作正常，可以开始抄表了。\n\n" + report.Summary);
            }
            else if (report.Health == DriverHealth.DeviceProblem)
            {
                Dialogs.Info($"{result.Message}。\n\n但设备仍未正常工作：{report.Summary}\n\n请拔下红外通讯头，等待 3 秒后重新插入，再点“重新检测”。" +
                             (result.RebootRequired ? "\n如仍不行，请重启电脑。" : ""));
            }
            else if (report.DriverInstalled)
            {
                Dialogs.Info($"{result.Message}，系统中已检测到 CH340 驱动。\n\n请插入红外通讯头，串口列表中会出现标有【CH340】的串口。" +
                             (result.RebootRequired ? "\n（提示需要重启电脑后生效）" : ""));
            }
            else
            {
                Main.ShowError(new MeterException(ErrorCode.DriverInstallFailed,
                    $"{result.Message}，但系统中仍未检测到 CH340 驱动。如果在安装程序中没有点击“安装”，请重新安装。"));
            }
        }
        finally
        {
            IsInstalling = false;
        }
    }

    private void Try(Action action, string what)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            Main.AddInfo($"{what}失败：{ex.Message}");
            Dialogs.Info($"{what}失败：{ex.Message}");
        }
    }
}
