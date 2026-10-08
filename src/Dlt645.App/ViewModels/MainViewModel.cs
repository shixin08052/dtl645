using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Dlt645.App.Infrastructure;
using Dlt645.App.Services;
using Dlt645.Core.Communication;
using Dlt645.Core.DataItems;
using Dlt645.Core.Errors;
using Dlt645.Core.Export;
using Dlt645.Core.Logging;
using Dlt645.Core.Protocol;
using Dlt645.Core.Simulation;
using Dlt645.Core.Transport;

namespace Dlt645.App.ViewModels;

/// <summary>连接状态（指示灯）。</summary>
public enum ConnectionState
{
    NotConnected,
    PortOpen,
    CommOk,
    Timeout,
    Fault,
}

/// <summary>主界面：串口连接、表号、功能页、报文监视、状态栏。</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private const int MaxMonitorEntries = 3000;

    private readonly Dispatcher _dispatcher;
    private readonly ConcurrentQueue<MonitorEntry> _pendingMonitor = new();
    private readonly DispatcherTimer _monitorFlushTimer;
    private readonly DispatcherTimer _deviceChangeDebounce;
    private readonly DispatcherTimer _portPollTimer;
    private string[] _lastPortNames = Array.Empty<string>();

    private ISerialTransport? _transport;
    private MeterClient? _client;
    private CancellationTokenSource? _cts;

    private PortInfo? _selectedPort;
    private string _portHint = string.Empty;
    private int _baudRate;
    private int _dataBits;
    private Parity _parity;
    private StopBits _stopBits;
    private int _timeoutMs;
    private int _retries;
    private bool _useSimulator;
    private bool _isConnected;
    private ConnectionState _state = ConnectionState.NotConnected;
    private string _meterAddressText = MeterAddress.WildcardText;
    private string? _addressError;
    private PageViewModel? _selectedPage;
    private bool _isBusy;
    private string _progressText = "就绪";
    private double _progressValue;
    private double _progressMax = 1;
    private string _lastCommText = "最近通讯：无";
    private ErrorBanner? _currentError;
    private bool _autoScrollMonitor = true;

    public MainViewModel(DataItemCatalog catalog, AppSettings settings, string? catalogWarning)
    {
        _dispatcher = Dispatcher.CurrentDispatcher;
        Catalog = catalog;
        Settings = settings;

        _baudRate = SerialSettings.BaudRates.Contains(settings.BaudRate) ? settings.BaudRate : SerialSettings.DefaultBaudRate;
        _dataBits = settings.DataBits is 7 or 8 ? settings.DataBits : SerialSettings.DefaultDataBits;
        _parity = settings.Parity;
        _stopBits = settings.StopBits is StopBits.One or StopBits.Two ? settings.StopBits : SerialSettings.DefaultStopBits;
        _timeoutMs = Math.Clamp(settings.TimeoutMs, 200, 10000);
        _retries = Math.Clamp(settings.Retries, 0, 5);
        _useSimulator = settings.UseSimulator;

        RefreshPortsCommand = new RelayCommand(() => RefreshPorts(userInitiated: true), () => !IsConnected || UseSimulator);
        RestoreDefaultsCommand = new RelayCommand(RestoreDefaults, () => !IsConnected);
        ToggleConnectionCommand = new RelayCommand(ToggleConnection, () => !IsBusy);
        ReadAddressCommand = new AsyncRelayCommand(ReadAddressAsync, () => !IsBusy);
        ResetAddressCommand = new RelayCommand(() => MeterAddressText = MeterAddress.WildcardText, () => !IsBusy);
        ReadCommonCommand = new AsyncRelayCommand(ReadCommonAsync, () => !IsBusy);
        ReadCurrentPageCommand = new RelayCommand(() => SelectedPage?.ReadCommand.Execute(null), () => !IsBusy && SelectedPage != null);
        CancelCommand = new RelayCommand(Cancel, () => IsBusy);
        ExportCsvCommand = new RelayCommand(() => ExportResults(excel: false), () => !IsBusy);
        ExportExcelCommand = new RelayCommand(() => ExportResults(excel: true), () => !IsBusy);
        DismissErrorCommand = new RelayCommand(() => CurrentError = null);
        CopyErrorCommand = new RelayCommand(() => { if (CurrentError != null) TrySetClipboard(CurrentError.ToString()); });
        CopyMonitorCommand = new RelayCommand(() => TrySetClipboard(string.Join(Environment.NewLine, MonitorEntries)), () => MonitorEntries.Count > 0);
        ClearMonitorCommand = new RelayCommand(() => MonitorEntries.Clear(), () => MonitorEntries.Count > 0);
        ExportMonitorCommand = new RelayCommand(ExportMonitor, () => MonitorEntries.Count > 0);
        ExportDiagnosticsCommand = new RelayCommand(ExportDiagnostics);

        CreatePages();

        _monitorFlushTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(100), DispatcherPriority.Background, (_, _) => FlushMonitor(), _dispatcher);
        _deviceChangeDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        _deviceChangeDebounce.Tick += (_, _) =>
        {
            _deviceChangeDebounce.Stop();
            RefreshPorts(userInitiated: false);
        };
        // 兜底：每 2 秒检查一次串口列表（部分系统收不到设备变化广播）
        _portPollTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => PollPorts(), _dispatcher);

        RefreshPorts(userInitiated: false);
        AddInfo($"程序启动，版本 {AppPaths.Version}；数据项配置：{catalog.Source}（{catalog.Items.Count} 项）");
        if (AppPaths.UsingFallbackDirectory)
            AddInfo($"程序目录不可写，日志与设置保存到：{AppPaths.DataDirectory}");
        if (catalogWarning != null)
        {
            AddMonitor(MonitorKind.Warning, catalogWarning);
            FileLogger.Warn(catalogWarning);
        }
    }

    // ================================================================ 基础数据

    public DataItemCatalog Catalog { get; }
    public AppSettings Settings { get; }
    public string WindowTitle => $"DL/T 645-2007 电表红外抄读工具 v{AppPaths.Version}（只读）";
    public string VersionText => $"v{AppPaths.Version}";
    public string CatalogText => $"数据项：{(Catalog.Source == "内置配置" ? "内置配置" : "外部 DataItems.json")}（{Catalog.Items.Count} 项）";
    public string LogDirectoryText => $"日志：{FileLogger.LogDirectory}";

    // ================================================================ 串口

    public ObservableCollection<PortInfo> Ports { get; } = new();

    public PortInfo? SelectedPort
    {
        get => _selectedPort;
        set => SetProperty(ref _selectedPort, value);
    }

    public string PortHint
    {
        get => _portHint;
        private set => SetProperty(ref _portHint, value);
    }

    public IReadOnlyList<int> BaudRates => SerialSettings.BaudRates;
    public IReadOnlyList<int> DataBitsOptions { get; } = new[] { 7, 8 };

    public IReadOnlyList<SelectableOption<Parity>> ParityOptions { get; } = new[]
    {
        new SelectableOption<Parity>(Parity.Even, "偶校验 E"),
        new SelectableOption<Parity>(Parity.None, "无校验 N"),
        new SelectableOption<Parity>(Parity.Odd, "奇校验 O"),
    };

    public IReadOnlyList<SelectableOption<StopBits>> StopBitsOptions { get; } = new[]
    {
        new SelectableOption<StopBits>(StopBits.One, "1"),
        new SelectableOption<StopBits>(StopBits.Two, "2"),
    };

    public int BaudRate { get => _baudRate; set => SetProperty(ref _baudRate, value); }
    public int DataBits { get => _dataBits; set => SetProperty(ref _dataBits, value); }
    public Parity Parity { get => _parity; set => SetProperty(ref _parity, value); }
    public StopBits StopBits { get => _stopBits; set => SetProperty(ref _stopBits, value); }

    /// <summary>超时时间（毫秒），修改后立即生效。</summary>
    public int TimeoutMs
    {
        get => _timeoutMs;
        set
        {
            // 超出范围时自动修正，并始终通知界面以显示修正后的值
            _timeoutMs = Math.Clamp(value, 200, 10000);
            OnPropertyChanged();
            if (_client != null) _client.Options.TimeoutMs = _timeoutMs;
        }
    }

    /// <summary>重试次数，修改后立即生效。</summary>
    public int Retries
    {
        get => _retries;
        set
        {
            _retries = Math.Clamp(value, 0, 5);
            OnPropertyChanged();
            if (_client != null) _client.Options.Retries = _retries;
        }
    }

    public bool UseSimulator
    {
        get => _useSimulator;
        set
        {
            if (SetProperty(ref _useSimulator, value))
            {
                OnPropertyChanged(nameof(ConnectButtonText));
                OnPropertyChanged(nameof(CanEditPort));
                UpdatePortHint();
            }
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (!SetProperty(ref _isConnected, value)) return;
            OnPropertyChanged(nameof(ConnectButtonText));
            OnPropertyChanged(nameof(CanEditPort));
            OnPropertyChanged(nameof(CanEditSerialParams));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    /// <summary>串口相关参数仅在未连接时可修改。</summary>
    public bool CanEditPort => !IsConnected && !UseSimulator;
    public bool CanEditSerialParams => !IsConnected;

    public string ConnectButtonText => IsConnected
        ? (UseSimulator ? "停止模拟" : "关闭串口")
        : (UseSimulator ? "启动模拟" : "打开串口");

    public ConnectionState State
    {
        get => _state;
        private set
        {
            if (SetProperty(ref _state, value)) OnPropertyChanged(nameof(StateText));
        }
    }

    public string StateText => State switch
    {
        ConnectionState.PortOpen => UseSimulator ? "模拟电表已就绪" : "已打开串口",
        ConnectionState.CommOk => "通讯正常",
        ConnectionState.Timeout => "通讯超时",
        ConnectionState.Fault => "串口异常断开",
        _ => "未连接",
    };

    // ================================================================ 表号

    public string MeterAddressText
    {
        get => _meterAddressText;
        set
        {
            if (!SetProperty(ref _meterAddressText, value)) return;
            AddressError = MeterAddress.TryParse(value, out _, out var err) ? null : err;
        }
    }

    public string? AddressError
    {
        get => _addressError;
        private set => SetProperty(ref _addressError, value);
    }

    /// <summary>表号框失去焦点时规范化（左补 0、通配大写）。</summary>
    public void NormalizeAddress()
    {
        if (MeterAddress.TryParse(MeterAddressText, out var a, out _)) MeterAddressText = a!.Text;
    }

    // ================================================================ 页面

    public ObservableCollection<PageViewModel> Pages { get; } = new();

    public PageViewModel? SelectedPage
    {
        get => _selectedPage;
        set
        {
            var old = _selectedPage;
            if (SetProperty(ref _selectedPage, value)) old?.OnDeactivated();
        }
    }

    private ReadPageViewModel? _commonPage;

    private void CreatePages()
    {
        _commonPage = new ReadPageViewModel(this, new ReadPageOptions
        {
            Title = "常用数据",
            Glyph = "",
            Description = "现场抄表最常用的数据（正向有功总及尖峰平谷、反向有功、日期时间、电压电流、功率、需量、事件次数等），点击上方“一键读取常用数据”或下方按钮读取。可在配置表中用 common 标记调整。",
            Items = Catalog.CommonItems.ToList(),
            IsCommonPage = true,
        });
        Pages.Add(_commonPage);

        void AddCategory(string category, string glyph, string description, bool autoRefresh = false, Func<DataItemDefinition, bool>? defaults = null)
        {
            var items = Catalog.ByCategory(category).ToList();
            if (items.Count == 0) return;
            Pages.Add(new ReadPageViewModel(this, new ReadPageOptions
            {
                Title = category,
                Glyph = glyph,
                Description = description,
                Items = items,
                SupportsAutoRefresh = autoRefresh,
                DefaultChecked = defaults,
            }));
        }

        AddCategory("电能量", "", "勾选电能种类、费率和月份后读取。月份选择“当前”对应 DI0=00，“上1月~上12月”对应 DI0=01~0C，可多选一次性批量读取。");
        AddCategory("最大需量", "", "最大需量及发生时间。“当前”和“上1~12结算日”对应 DI0=00~0C。");
        AddCategory("实时变量", "", "电压、电流、功率、功率因数、频率等瞬时量。可开启“自动刷新”连续读取。", autoRefresh: true);
        AddCategory("电表参数", "", "日期时间、通信地址、表号、资产编号、铭牌参数等（只读）。");
        AddCategory("事件记录", "", "失压、断相、掉电、开表盖、编程等事件的次数与最近 N 次记录。记录中未在配置表定义的部分以原始数据显示。",
            defaults: d => d.History == HistoryKind.None && d.Fields.Count == 1);
        AddCategory("冻结数据", "", "定时冻结、瞬时冻结、整点冻结、日冻结。选择读取最近 N 次。",
            defaults: d => d.Id.StartsWith("F.Day", StringComparison.Ordinal));

        // 外部配置中新增的分类也自动生成页面
        foreach (var cat in Catalog.Categories.Where(c => Pages.All(p => p.Title != c)))
            AddCategory(cat, "", $"配置表中的自定义分类：{cat}");

        Pages.Add(new CustomDiPageViewModel(this));
        _selectedPage = Pages[0];
    }

    // ================================================================ 忙碌 / 进度

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            OnPropertyChanged(nameof(IsIdle));
            CommandManager.InvalidateRequerySuggested();
        }
    }

    public bool IsIdle => !IsBusy;

    public string ProgressText { get => _progressText; private set => SetProperty(ref _progressText, value); }
    public double ProgressValue { get => _progressValue; private set => SetProperty(ref _progressValue, value); }
    public double ProgressMax { get => _progressMax; private set => SetProperty(ref _progressMax, value); }
    public string LastCommText { get => _lastCommText; private set => SetProperty(ref _lastCommText, value); }

    // ================================================================ 错误提示

    public ErrorBanner? CurrentError
    {
        get => _currentError;
        private set => SetProperty(ref _currentError, value);
    }

    public void ShowError(MeterException ex, bool warning = false)
    {
        CurrentError = new ErrorBanner(ex, warning);
        if (warning) FileLogger.Warn(ex.Message);
        else FileLogger.Error(ex.Message, ex.InnerException);
    }

    // ================================================================ 报文监视

    public ObservableCollection<MonitorEntry> MonitorEntries { get; } = new();

    public bool AutoScrollMonitor
    {
        get => _autoScrollMonitor;
        set => SetProperty(ref _autoScrollMonitor, value);
    }

    public void AddInfo(string text)
    {
        AddMonitor(MonitorKind.Info, text);
        FileLogger.Info(text);
    }

    private void AddMonitor(MonitorKind kind, string text) =>
        _pendingMonitor.Enqueue(new MonitorEntry(DateTime.Now, kind, null, text));

    private void OnFrameLogged(MonitorEntry e)
    {
        _pendingMonitor.Enqueue(e);
        FileLogger.Frame(e.ToString());
    }

    private void FlushMonitor()
    {
        if (_pendingMonitor.IsEmpty) return;
        while (_pendingMonitor.TryDequeue(out var e)) MonitorEntries.Add(e);
        while (MonitorEntries.Count > MaxMonitorEntries) MonitorEntries.RemoveAt(0);
    }

    // ================================================================ 命令

    public ICommand RefreshPortsCommand { get; }
    public ICommand RestoreDefaultsCommand { get; }
    public ICommand ToggleConnectionCommand { get; }
    public ICommand ReadAddressCommand { get; }
    public ICommand ResetAddressCommand { get; }
    public ICommand ReadCommonCommand { get; }
    public ICommand ReadCurrentPageCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand ExportCsvCommand { get; }
    public ICommand ExportExcelCommand { get; }
    public ICommand DismissErrorCommand { get; }
    public ICommand CopyErrorCommand { get; }
    public ICommand CopyMonitorCommand { get; }
    public ICommand ClearMonitorCommand { get; }
    public ICommand ExportMonitorCommand { get; }
    public ICommand ExportDiagnosticsCommand { get; }

    // ================================================================ 串口枚举

    /// <summary>窗口收到 WM_DEVICECHANGE 时调用（去抖后刷新）。</summary>
    public void NotifyDeviceChanged()
    {
        _deviceChangeDebounce.Stop();
        _deviceChangeDebounce.Start();
    }

    private void PollPorts()
    {
        string[] names;
        try { names = SerialPort.GetPortNames().OrderBy(n => n).ToArray(); }
        catch { return; }
        if (!names.SequenceEqual(_lastPortNames)) RefreshPorts(userInitiated: false);
    }

    public void RefreshPorts(bool userInitiated)
    {
        var previous = SelectedPort?.PortName ?? Settings.PortName;
        var list = PortEnumerator.Enumerate();
        _lastPortNames = list.Select(p => p.PortName).OrderBy(n => n).ToArray();

        Ports.Clear();
        foreach (var p in list) Ports.Add(p);

        SelectedPort = list.FirstOrDefault(p => string.Equals(p.PortName, previous, StringComparison.OrdinalIgnoreCase))
                       ?? list.FirstOrDefault(p => p.IsCh340)
                       ?? list.FirstOrDefault();
        UpdatePortHint();

        if (userInitiated)
        {
            AddInfo($"刷新串口：共 {list.Count} 个" + (list.Count > 0 ? "（" + string.Join("，", list.Select(p => p.IsCh340 ? p.PortName + "[CH340]" : p.PortName)) + "）" : ""));
        }

        // 已打开的真实串口消失 → 判定为拔出
        if (IsConnected && !UseSimulator && _transport is SerialPortTransport spt &&
            !list.Any(p => string.Equals(p.PortName, spt.Settings.PortName, StringComparison.OrdinalIgnoreCase)))
        {
            HandlePortFault(new MeterException(ErrorCode.PortDisconnected, $"{spt.Settings.PortName} 已从系统中移除（红外头被拔出？）"));
        }
    }

    private void UpdatePortHint()
    {
        if (UseSimulator)
            PortHint = string.Empty;
        else if (Ports.Count == 0)
            PortHint = "未检测到任何串口：请插入红外通讯头，并确认已安装 CH340 驱动（设备管理器 → 端口 中应显示 USB-SERIAL CH340）。";
        else if (!Ports.Any(p => p.IsCh340))
            PortHint = "未检测到 CH340 设备：请检查红外通讯头是否插好、CH340 驱动是否安装。如使用其他芯片的通讯头，可直接选择对应串口。";
        else
            PortHint = string.Empty;
    }

    private void RestoreDefaults()
    {
        BaudRate = SerialSettings.DefaultBaudRate;
        DataBits = SerialSettings.DefaultDataBits;
        Parity = SerialSettings.DefaultParity;
        StopBits = SerialSettings.DefaultStopBits;
        TimeoutMs = CommOptions.DefaultTimeoutMs;
        Retries = CommOptions.DefaultRetries;
        AddInfo("串口参数已恢复默认：2400bps，8 数据位，偶校验 E，1 停止位，超时 1500ms，重试 2 次");
    }

    // ================================================================ 连接

    private void ToggleConnection()
    {
        if (IsConnected) Disconnect("用户关闭");
        else Connect();
    }

    private void Connect()
    {
        try
        {
            ISerialTransport transport;
            if (UseSimulator)
            {
                transport = new SimulatedTransport(new SimulatedMeter(Catalog));
            }
            else
            {
                if (SelectedPort is null)
                {
                    ShowError(new MeterException(Ports.Count == 0 ? ErrorCode.Ch340NotFound : ErrorCode.PortNotSelected));
                    return;
                }
                transport = new SerialPortTransport(new SerialSettings
                {
                    PortName = SelectedPort.PortName,
                    BaudRate = BaudRate,
                    DataBits = DataBits,
                    Parity = Parity,
                    StopBits = StopBits,
                });
            }

            transport.Open();
            _transport = transport;
            _client = new MeterClient(transport, new CommOptions { TimeoutMs = TimeoutMs, Retries = Retries })
            {
                DiNamer = Catalog.DescribeDi,
            };
            _client.FrameLogged += OnFrameLogged;
            _client.TransactionCompleted += OnTransactionCompleted;

            IsConnected = true;
            State = ConnectionState.PortOpen;
            CurrentError = null;
            AddInfo(UseSimulator
                ? "已启动模拟电表（表号 000012345678，含红外回显模拟），可直接演示各项读取"
                : $"已打开串口 {transport.DisplayName}" + (SelectedPort?.IsCh340 == true ? "（CH340）" : ""));
            SaveSettings();
        }
        catch (MeterException ex)
        {
            State = ConnectionState.NotConnected;
            ShowError(ex);
            AddMonitor(MonitorKind.Error, ex.Message);
        }
    }

    public void Disconnect(string reason)
    {
        _cts?.Cancel();
        foreach (var p in Pages) p.OnDeactivated();
        if (_client != null)
        {
            _client.FrameLogged -= OnFrameLogged;
            _client.TransactionCompleted -= OnTransactionCompleted;
        }
        try { _transport?.Close(); } catch { }
        _transport = null;
        _client = null;
        if (IsConnected) AddInfo($"已断开（{reason}）");
        IsConnected = false;
        State = ConnectionState.NotConnected;
    }

    private void HandlePortFault(MeterException ex)
    {
        bool wasConnected = IsConnected;
        Disconnect("串口异常");
        State = ConnectionState.Fault;
        if (wasConnected || ex.IsPortFault) ShowError(ex);
        AddMonitor(MonitorKind.Error, ex.Message);
    }

    private void OnTransactionCompleted(TransactionInfo info)
    {
        _dispatcher.BeginInvoke(() =>
        {
            var ms = (long)info.Elapsed.TotalMilliseconds;
            LastCommText = info.Success
                ? $"最近通讯：成功 · {info.Operation} · 耗时 {ms} ms · {DateTime.Now:HH:mm:ss}"
                : $"最近通讯：失败 · {info.Operation} · {info.Code.ToCodeString()} {ErrorCatalog.Get(info.Code).Title} · 耗时 {ms} ms · {DateTime.Now:HH:mm:ss}";

            if (!IsConnected) return;
            State = info.Code switch
            {
                ErrorCode.None or ErrorCode.MeterErrorResponse or ErrorCode.AddressMismatch
                    or ErrorCode.DiMismatch or ErrorCode.UnexpectedResponse => ConnectionState.CommOk,
                ErrorCode.Timeout or ErrorCode.ChecksumError or ErrorCode.IncompleteFrame
                    or ErrorCode.FrameFormatError or ErrorCode.FollowFrameFailed => ConnectionState.Timeout,
                ErrorCode.PortDisconnected or ErrorCode.PortWriteFailed => ConnectionState.Fault,
                _ => State,
            };
        });
    }

    // ================================================================ 读取

    private bool EnsureReady(out MeterClient client, out MeterAddress address)
    {
        client = null!;
        address = null!;
        if (IsBusy) return false;
        if (!IsConnected || _client is null)
        {
            ShowError(new MeterException(ErrorCode.NotConnected));
            return false;
        }
        if (!MeterAddress.TryParse(MeterAddressText, out var a, out var err))
        {
            ShowError(new MeterException(ErrorCode.InvalidAddressInput, err));
            return false;
        }
        MeterAddressText = a!.Text;
        client = _client;
        address = a;
        return true;
    }

    /// <summary>统一的“忙碌”包装：显示进度、可取消、禁止重复点击、统一处理异常。</summary>
    private async Task RunExclusiveAsync(string title, Func<CancellationToken, Task> work)
    {
        IsBusy = true;
        _cts = new CancellationTokenSource();
        ProgressText = title + "…";
        ProgressValue = 0;
        ProgressMax = 1;
        try
        {
            await work(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            ProgressText = "已取消";
            AddInfo($"{title}：已取消");
        }
        catch (MeterException ex) when (ex.IsPortFault || ex.Code == ErrorCode.NotConnected)
        {
            ProgressText = "串口异常";
            HandlePortFault(ex);
        }
        catch (MeterException ex)
        {
            ProgressText = $"失败：{ex.Code.ToCodeString()} {ex.Info.Title}";
            ShowError(ex);
        }
        catch (Exception ex)
        {
            ProgressText = "发生未知错误";
            FileLogger.Error($"{title} 发生未知错误", ex);
            ShowError(new MeterException(ErrorCode.Unknown, ex.Message, ex));
        }
        finally
        {
            _cts.Dispose();
            _cts = null;
            IsBusy = false;
        }
    }

    private void Cancel()
    {
        if (_cts is { IsCancellationRequested: false })
        {
            _cts.Cancel();
            ProgressText = "正在取消…";
        }
    }

    private async Task ReadAddressAsync()
    {
        if (!IsConnected || _client is null)
        {
            ShowError(new MeterException(ErrorCode.NotConnected));
            return;
        }
        if (IsBusy) return;
        var client = _client;
        await RunExclusiveAsync("读取表号", async ct =>
        {
            AddInfo("使用公共地址 AAAAAAAAAAAA 读取通信地址（控制码 13H）");
            var addr = await client.ReadAddressAsync(ct);
            MeterAddressText = addr.Text;
            CurrentError = null;
            ProgressText = $"读取表号成功：{addr.Text}";
            AddInfo($"读取表号成功：{addr.Text}，已填入表号框");
        });
    }

    private async Task ReadCommonAsync()
    {
        if (_commonPage is null) return;
        SelectedPage = _commonPage;
        await ReadPageAsync(_commonPage);
    }

    /// <summary>读取某个数据页当前勾选的所有数据项。</summary>
    public async Task ReadPageAsync(ReadPageViewModel page)
    {
        var invalid = page.ValidateSelection();
        if (invalid != null)
        {
            page.StopAutoRefresh();
            Dialogs.Info(invalid);
            return;
        }
        if (!EnsureReady(out var client, out var address))
        {
            page.StopAutoRefresh();
            return;
        }
        var requests = page.BuildRequests();

        await RunExclusiveAsync($"读取{page.Title}", async ct =>
        {
            page.Results.Clear();
            page.LastMeterNo = address.Text;
            page.LastReadTime = DateTime.Now;
            ProgressMax = requests.Count;
            if (!page.IsAutoRefresh) AddInfo($"开始读取【{page.Title}】，共 {requests.Count} 次通讯，表号 {address.Text}");

            int ok = 0, fail = 0, consecutiveTimeouts = 0;
            MeterException? lastError = null;
            bool stoppedEarly = false;
            for (int i = 0; i < requests.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var req = requests[i];
                ProgressText = $"正在读取 {i + 1}/{requests.Count}：{req.Describe()}";
                try
                {
                    var res = await client.ReadDataAsync(address, req.Di, ct);
                    var fields = DataItemDecoder.Decode(req.Item, res.Data);
                    page.AddRows(ReadResultRow.FromSuccess(req, res, fields));
                    if (res.Warnings.Count > 0) ShowError(res.Warnings[0], warning: true);
                    // 应答中带回的实际表号，便于导出
                    if (address.HasWildcard) page.LastMeterNo = res.ResponseAddress.Text;
                    ok++;
                    consecutiveTimeouts = 0;
                }
                catch (MeterException ex) when (!ex.IsPortFault && ex.Code != ErrorCode.NotConnected)
                {
                    page.AddRows(new[] { ReadResultRow.FromError(req, ex) });
                    fail++;
                    lastError = ex;
                    consecutiveTimeouts = ex.Code == ErrorCode.Timeout ? consecutiveTimeouts + 1 : 0;
                    if (consecutiveTimeouts >= 3 && i < requests.Count - 1)
                    {
                        stoppedEarly = true;
                        lastError = new MeterException(ErrorCode.Timeout,
                            $"连续 3 项无应答，已停止本次读取（完成 {i + 1}/{requests.Count}）。{ex.Detail}");
                        break;
                    }
                }
                ProgressValue = i + 1;
            }

            ProgressText = stoppedEarly
                ? $"已停止：连续无应答（成功 {ok} 项，失败 {fail} 项）"
                : $"【{page.Title}】读取完成：成功 {ok} 项，失败 {fail} 项";
            if (!page.IsAutoRefresh || fail > 0) AddInfo(ProgressText);

            if (lastError != null)
            {
                // 全部是“无请求数据”时只作为警告
                bool onlyUnsupported = lastError.Code == ErrorCode.MeterErrorResponse && ok > 0;
                ShowError(lastError, warning: onlyUnsupported);
            }
            else if (CurrentError is { IsWarning: false })
            {
                CurrentError = null;
            }
        });
    }

    /// <summary>自定义数据标识读取。</summary>
    public async Task ReadCustomAsync(CustomDiPageViewModel page)
    {
        if (!page.TryGetDi(out var di))
        {
            ShowError(new MeterException(ErrorCode.InvalidDiInput, page.DiError));
            return;
        }
        if (!EnsureReady(out var client, out var address)) return;

        await RunExclusiveAsync($"读取 DI {DataId.Format(di)}", async ct =>
        {
            page.LastMeterNo = address.Text;
            page.LastReadTime = DateTime.Now;
            try
            {
                var res = await client.ReadDataAsync(address, di, ct);
                if (address.HasWildcard) page.LastMeterNo = res.ResponseAddress.Text;
                var (fields, name) = page.Interpret(di, res.Data, Catalog);
                page.LastRawText = res.Data.Length == 0
                    ? "（应答中 DI 之后没有数据）"
                    : $"接收顺序（已减33H）：{Hex.ToHex(res.Data)}    高字节在前：{Hex.ToHexReversed(res.Data, " ")}    共 {res.Data.Length} 字节" +
                      (res.FrameCount > 1 ? $"，{res.FrameCount} 帧" : "");
                var warn = res.Warnings.Count > 0 ? "（" + string.Join("；", res.Warnings.Select(w => w.Code.ToCodeString() + " " + w.Info.Title)) + "）" : "";
                page.AddRows(fields.Select(f => new ReadResultRow
                {
                    Item = name,
                    Field = f.Name,
                    Di = DataId.Format(di),
                    Value = f.Text,
                    Unit = f.Unit,
                    Number = f.Number,
                    Raw = f.RawHex,
                    Status = (f.Valid ? "成功" : "数据无效") + warn,
                    IsWarning = !f.Valid || warn.Length > 0,
                    ElapsedMs = (long)res.Elapsed.TotalMilliseconds,
                }));
                if (res.Warnings.Count > 0) ShowError(res.Warnings[0], warning: true);
                ProgressText = $"读取 {DataId.Format(di)} 成功，{res.Data.Length} 字节";
            }
            catch (MeterException ex) when (!ex.IsPortFault && ex.Code != ErrorCode.NotConnected)
            {
                page.LastRawText = string.Empty;
                page.AddRows(new[]
                {
                    new ReadResultRow
                    {
                        Item = Catalog.DescribeDi(di) ?? $"自定义 {DataId.Format(di)}",
                        Di = DataId.Format(di),
                        Value = "--",
                        Status = ReadResultRow.StatusOf(ex),
                        IsError = true,
                    },
                });
                throw;
            }
        });
    }

    // ================================================================ 导出

    private void ExportResults(bool excel)
    {
        var page = SelectedPage;
        if (page is null || page.Results.Count == 0)
        {
            Dialogs.Info("当前页面没有读取结果，请先读取数据。");
            return;
        }
        var meterNo = page.LastMeterNo ?? MeterAddressText;
        var time = page.LastReadTime ?? DateTime.Now;
        var name = $"{page.Title}_{meterNo}_{time:yyyyMMdd_HHmmss}";
        var path = excel
            ? Dialogs.SaveFile("导出为 Excel", "Excel 工作簿 (*.xlsx)|*.xlsx", name + ".xlsx")
            : Dialogs.SaveFile("导出为 CSV", "CSV 文件 (*.csv)|*.csv", name + ".csv");
        if (path is null) return;

        try
        {
            var header = new ExportHeader($"DL/T645 电表读取结果 - {page.Title}", meterNo, time,
                UseSimulator ? "模拟电表数据" : null);
            if (excel) ResultExporter.ExportXlsx(path, header, page.Results);
            else ResultExporter.ExportCsv(path, header, page.Results);
            AddInfo($"已导出 {page.Results.Count} 行到 {path}");
            Dialogs.ExportDone(path);
        }
        catch (Exception ex)
        {
            ShowError(new MeterException(ErrorCode.ExportFailed, ex.Message, ex));
        }
    }

    private void ExportMonitor()
    {
        var path = Dialogs.SaveFile("导出报文", "文本文件 (*.txt)|*.txt", $"报文记录_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        if (path is null) return;
        try
        {
            File.WriteAllLines(path, MonitorEntries.Select(e => e.ToString()), new UTF8Encoding(true));
            Dialogs.ExportDone(path);
        }
        catch (Exception ex)
        {
            ShowError(new MeterException(ErrorCode.ExportFailed, ex.Message, ex));
        }
    }

    private void ExportDiagnostics()
    {
        var path = Dialogs.SaveFile("导出诊断信息", "压缩包 (*.zip)|*.zip", $"诊断信息_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        if (path is null) return;
        try
        {
            FlushMonitor();
            DiagnosticsExporter.Export(path, BuildDiagnosticsSummary(), MonitorEntries.Select(e => e.ToString()).ToList());
            AddInfo($"诊断信息已导出：{path}");
            Dialogs.ExportDone(path);
        }
        catch (Exception ex)
        {
            ShowError(new MeterException(ErrorCode.ExportFailed, ex.Message, ex));
        }
    }

    public string BuildDiagnosticsSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("==== DL/T645 红外抄表工具 诊断信息 ====");
        sb.AppendLine($"导出时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"软件版本：{AppPaths.Version}");
        sb.AppendLine($"操作系统：{Environment.OSVersion}（{(Environment.Is64BitOperatingSystem ? "64" : "32")} 位）");
        sb.AppendLine($".NET 运行时：{Environment.Version}");
        sb.AppendLine($"程序目录：{AppPaths.ProgramDirectory}");
        sb.AppendLine($"数据目录：{AppPaths.DataDirectory}");
        sb.AppendLine($"数据项配置：{Catalog.Source}（{Catalog.Items.Count} 项）");
        sb.AppendLine();
        sb.AppendLine("---- 串口参数 ----");
        sb.AppendLine($"模式：{(UseSimulator ? "模拟电表" : "真实串口")}");
        sb.AppendLine($"串口：{SelectedPort?.Display ?? "（未选择）"}");
        sb.AppendLine($"波特率/数据位/校验/停止位：{BaudRate} / {DataBits} / {SerialSettings.ParityText(Parity)} / {SerialSettings.StopBitsText(StopBits)}");
        sb.AppendLine($"超时：{TimeoutMs} ms，重试：{Retries} 次");
        sb.AppendLine($"连接状态：{StateText}");
        sb.AppendLine($"表号：{MeterAddressText}");
        sb.AppendLine(LastCommText);
        sb.AppendLine();
        sb.AppendLine("---- 系统中的串口 ----");
        if (Ports.Count == 0) sb.AppendLine("（无）");
        foreach (var p in Ports) sb.AppendLine(p.Display);
        sb.AppendLine();
        sb.AppendLine("---- 当前错误提示 ----");
        sb.AppendLine(CurrentError?.ToString() ?? "（无）");
        if (FileLogger.LastWriteError != null) sb.AppendLine($"日志写入失败：{FileLogger.LastWriteError}");
        return sb.ToString();
    }

    // ================================================================ 其他

    private static void TrySetClipboard(string text)
    {
        try
        {
            Clipboard.SetText(text);
        }
        catch (Exception ex)
        {
            FileLogger.Warn($"复制到剪贴板失败：{ex.Message}");
        }
    }

    public void SaveSettings()
    {
        if (!UseSimulator && SelectedPort != null) Settings.PortName = SelectedPort.PortName;
        Settings.BaudRate = BaudRate;
        Settings.DataBits = DataBits;
        Settings.Parity = Parity;
        Settings.StopBits = StopBits;
        Settings.TimeoutMs = TimeoutMs;
        Settings.Retries = Retries;
        Settings.UseSimulator = UseSimulator;
        Settings.Save();
    }

    public void Dispose()
    {
        _monitorFlushTimer.Stop();
        _portPollTimer.Stop();
        _deviceChangeDebounce.Stop();
        Disconnect("程序退出");
        FlushMonitor();
    }
}
