using System.Collections.ObjectModel;
using System.Windows.Input;
using Dlt645.App.Infrastructure;
using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;
using Dlt645.Core.Simulation;

namespace Dlt645.App.ViewModels;

/// <summary>模拟电表中的一条自定义应答数据。</summary>
public sealed class CustomDataEntry
{
    public CustomDataEntry(uint di, byte[] data, string preview)
    {
        Di = di;
        Data = data;
        Preview = preview;
    }

    public uint Di { get; }
    public byte[] Data { get; }
    public string DiText => DataId.Format(Di);
    public string DataText => Hex.ToHex(Data);
    public string Preview { get; }
}

/// <summary>
/// “模拟电表”页：不接硬件即可演示和测试解析逻辑。
/// 设置在未启动时保存，启动模拟电表时应用；运行中修改立即生效。
/// </summary>
public sealed class SimulatorPageViewModel : PageViewModel
{
    public const string DefaultAddress = "000012345678";

    private string _addressText = DefaultAddress;
    private string? _addressError;
    private bool _echo = true;
    private int _responseDelayMs = 80;
    private int _maxDataPerFrame = 48;
    private bool _noResponse;
    private SelectableOption<byte?> _selectedErrorWord;
    private string _customDi = "00010000";
    private string _customData = "00 00 12 34";
    private bool _customMsbFirst = true;
    private string _customPreview = string.Empty;
    private bool _customPreviewOk;
    private string _faultStatus = string.Empty;

    public SimulatorPageViewModel(MainViewModel main)
        : base(main, "模拟电表", "",
            "不接红外头和电表也能演示全部功能、测试解析逻辑。启动后表号为下方设置的模拟表号，会模拟红外回显；" +
            "数据超过单帧上限时自动分成后续帧（12H/B1H）。可以注入各种通讯故障，或给任意数据标识指定应答数据，再到各功能页读取查看解析结果。")
    {
        ErrorWordOptions = new[]
        {
            new SelectableOption<byte?>(null, "正常应答（不注入）"),
            new SelectableOption<byte?>(0x01, "01H 其他错误"),
            new SelectableOption<byte?>(0x02, "02H 无请求数据"),
            new SelectableOption<byte?>(0x04, "04H 密码错或未授权"),
            new SelectableOption<byte?>(0x08, "08H 通信速率不能更改"),
            new SelectableOption<byte?>(0x10, "10H 年时区数超"),
            new SelectableOption<byte?>(0x20, "20H 日时段数超"),
            new SelectableOption<byte?>(0x40, "40H 费率数超"),
        };
        _selectedErrorWord = ErrorWordOptions[0];

        StartCommand = new RelayCommand(() => Main.StartSimulator(), () => !Main.IsBusy && !Main.IsConnected);
        ApplyAddressCommand = new RelayCommand(ApplyAddress);
        InjectChecksumCommand = new RelayCommand(() => Inject(t => t.CorruptNextResponses++, "下一次应答的校验和将被破坏（E202），重试后会恢复"));
        InjectTruncateCommand = new RelayCommand(() => Inject(t => t.TruncateNextResponses++, "下一次应答只发送一半字节（E203 帧不完整），重试后会恢复"));
        SetCustomDataCommand = new RelayCommand(SetCustomData);
        RemoveCustomDataCommand = new RelayCommand(p =>
        {
            if (p is not CustomDataEntry e) return;
            CustomEntries.Remove(e);
            Main.Simulator?.Meter.ClearData(e.Di);
            Main.AddInfo($"模拟电表：已删除 {e.DiText} 的自定义应答数据");
        });
        ClearCustomDataCommand = new RelayCommand(() =>
        {
            CustomEntries.Clear();
            Main.Simulator?.Meter.ClearAllData();
            Main.AddInfo("模拟电表：已清除全部自定义应答数据");
        }, () => CustomEntries.Count > 0);
        ResetFaultsCommand = new RelayCommand(ResetFaults);

        UpdatePreview();
    }

    public override ICommand ReadCommand => StartCommand;

    // ---------------------------------------------------------------- 基本设置

    public string AddressText
    {
        get => _addressText;
        set
        {
            if (!SetProperty(ref _addressText, value)) return;
            AddressError = MeterAddress.TryParse(value, out var a, out var err) && !a!.HasWildcard
                ? null
                : err ?? "模拟表号不能包含 AA 通配";
        }
    }

    public string? AddressError
    {
        get => _addressError;
        private set => SetProperty(ref _addressError, value);
    }

    public bool Echo
    {
        get => _echo;
        set
        {
            if (!SetProperty(ref _echo, value)) return;
            if (Main.Simulator is { } t) t.Echo = value;
        }
    }

    public int ResponseDelayMs
    {
        get => _responseDelayMs;
        set
        {
            _responseDelayMs = Math.Clamp(value, 0, 5000);
            OnPropertyChanged();
            if (Main.Simulator is { } t) t.ResponseDelayMs = _responseDelayMs;
        }
    }

    public int MaxDataPerFrame
    {
        get => _maxDataPerFrame;
        set
        {
            _maxDataPerFrame = Math.Clamp(value, 8, 190);
            OnPropertyChanged();
            if (Main.Simulator is { } t) t.Meter.MaxDataPerFrame = _maxDataPerFrame;
        }
    }

    // ---------------------------------------------------------------- 故障模拟

    public bool NoResponse
    {
        get => _noResponse;
        set
        {
            if (!SetProperty(ref _noResponse, value)) return;
            if (Main.Simulator is { } t) t.Respond = !value;
            if (Main.IsConnected) Main.AddInfo(value ? "模拟电表：已设置为不应答（演示超时 E201）" : "模拟电表：恢复正常应答");
        }
    }

    public IReadOnlyList<SelectableOption<byte?>> ErrorWordOptions { get; }

    public SelectableOption<byte?> SelectedErrorWord
    {
        get => _selectedErrorWord;
        set
        {
            if (!SetProperty(ref _selectedErrorWord, value)) return;
            if (Main.Simulator is { } t) t.Meter.ForcedErrorWord = value.Value;
            if (Main.IsConnected)
                Main.AddInfo(value.Value is null ? "模拟电表：恢复正常应答" : $"模拟电表：所有读数据请求将返回异常应答 D1H，错误字 {value.Label}");
        }
    }

    public string FaultStatus
    {
        get => _faultStatus;
        private set => SetProperty(ref _faultStatus, value);
    }

    // ---------------------------------------------------------------- 自定义应答数据

    public string CustomDi
    {
        get => _customDi;
        set
        {
            if (SetProperty(ref _customDi, value)) UpdatePreview();
        }
    }

    public string CustomData
    {
        get => _customData;
        set
        {
            if (SetProperty(ref _customData, value)) UpdatePreview();
        }
    }

    /// <summary>输入按“高字节在前”的书写顺序（与数值的阅读顺序一致），发送时自动反转为低字节在前。</summary>
    public bool CustomMsbFirst
    {
        get => _customMsbFirst;
        set
        {
            if (SetProperty(ref _customMsbFirst, value)) UpdatePreview();
        }
    }

    public string CustomPreview
    {
        get => _customPreview;
        private set => SetProperty(ref _customPreview, value);
    }

    public bool CustomPreviewOk
    {
        get => _customPreviewOk;
        private set => SetProperty(ref _customPreviewOk, value);
    }

    public ObservableCollection<CustomDataEntry> CustomEntries { get; } = new();

    // ---------------------------------------------------------------- 命令

    public ICommand StartCommand { get; }
    public ICommand ApplyAddressCommand { get; }
    public ICommand InjectChecksumCommand { get; }
    public ICommand InjectTruncateCommand { get; }
    public ICommand SetCustomDataCommand { get; }
    public ICommand RemoveCustomDataCommand { get; }
    public ICommand ClearCustomDataCommand { get; }
    public ICommand ResetFaultsCommand { get; }

    // ---------------------------------------------------------------- 逻辑

    /// <summary>创建模拟电表与模拟串口，应用当前全部设置。</summary>
    public SimulatedTransport CreateTransport()
    {
        var address = MeterAddress.TryParse(AddressText, out var a, out _) && !a!.HasWildcard ? a : MeterAddress.Parse(DefaultAddress);
        var meter = new SimulatedMeter(Main.Catalog, address)
        {
            MaxDataPerFrame = MaxDataPerFrame,
            ForcedErrorWord = SelectedErrorWord.Value,
        };
        foreach (var e in CustomEntries) meter.SetData(e.Di, e.Data);
        return new SimulatedTransport(meter)
        {
            Echo = Echo,
            ResponseDelayMs = ResponseDelayMs,
            Respond = !NoResponse,
        };
    }

    public string SettingsSummary =>
        $"表号 {AddressText}，回显{(Echo ? "开" : "关")}，延迟 {ResponseDelayMs}ms，单帧 {MaxDataPerFrame} 字节" +
        (NoResponse ? "，不应答" : "") +
        (SelectedErrorWord.Value is byte b ? $"，异常应答 {b:X2}H" : "") +
        (CustomEntries.Count > 0 ? $"，自定义数据 {CustomEntries.Count} 项" : "");

    private void ApplyAddress()
    {
        if (AddressError != null || !MeterAddress.TryParse(AddressText, out var a, out _))
        {
            Services.Dialogs.Info(AddressError ?? "模拟表号格式不正确");
            return;
        }
        AddressText = a!.Text;
        if (Main.Simulator is { } t)
        {
            t.Meter.Address = a;
            Main.AddInfo($"模拟电表表号已改为 {a.Text}。可点“读取表号”验证；若表号框不是该表号或 AAAAAAAAAAAA，读取将超时。");
        }
        else
        {
            Main.AddInfo($"模拟表号设为 {a.Text}，启动模拟电表后生效");
        }
    }

    private void Inject(Action<SimulatedTransport> action, string message)
    {
        if (Main.Simulator is not { } t)
        {
            Services.Dialogs.Info("请先启动模拟电表。");
            return;
        }
        action(t);
        FaultStatus = message;
        Main.AddInfo("模拟电表：" + message);
    }

    private void ResetFaults()
    {
        NoResponse = false;
        SelectedErrorWord = ErrorWordOptions[0];
        if (Main.Simulator is { } t)
        {
            t.CorruptNextResponses = 0;
            t.TruncateNextResponses = 0;
        }
        FaultStatus = "已恢复正常应答";
    }

    private bool TryParseCustom(out uint di, out byte[] wire, out string? error)
    {
        wire = Array.Empty<byte>();
        if (!DataId.TryParse(CustomDi, out di, out error)) return false;
        if (string.IsNullOrWhiteSpace(CustomData))
        {
            error = null; // 空数据也允许（应答只有 DI）
            return true;
        }
        if (!Hex.TryParse(CustomData, out var bytes))
        {
            error = "数据应为十六进制字节，例如 00 00 12 34";
            return false;
        }
        if (bytes.Length > 190)
        {
            error = "数据过长（最多 190 字节）";
            return false;
        }
        if (CustomMsbFirst) Array.Reverse(bytes);
        wire = bytes;
        return true;
    }

    private void UpdatePreview()
    {
        if (!TryParseCustom(out var di, out var wire, out var error))
        {
            CustomPreview = error ?? string.Empty;
            CustomPreviewOk = false;
            return;
        }
        CustomPreview = Describe(di, wire);
        CustomPreviewOk = true;
    }

    private string Describe(uint di, byte[] wire)
    {
        var order = $"实际发送（低字节在前，未加33H）：{(wire.Length == 0 ? "无数据" : Hex.ToHex(wire))}";
        var match = Main.Catalog.Find(di);
        if (match is null)
            return $"{order}\n配置表中没有 {DataId.Format(di)}，读取时将显示原始数据并尝试按 BCD/ASCII 解析";
        var fields = DataItemDecoder.Decode(match.Item, wire);
        var parts = fields.Select(f =>
            (string.IsNullOrEmpty(f.Name) ? "" : f.Name + "=") + f.Text + (f.Unit.Length > 0 ? " " + f.Unit : "") + (f.Valid ? "" : "（无效）"));
        return $"{order}\n按配置表解析【{match.Describe()}】：{string.Join("；", parts)}";
    }

    private void SetCustomData()
    {
        if (!TryParseCustom(out var di, out var wire, out var error))
        {
            Services.Dialogs.Info(error ?? "输入有误");
            return;
        }
        var existing = CustomEntries.FirstOrDefault(e => e.Di == di);
        if (existing != null) CustomEntries.Remove(existing);
        var entry = new CustomDataEntry(di, wire, Describe(di, wire).Split('\n')[^1]);
        CustomEntries.Add(entry);
        Main.Simulator?.Meter.SetData(di, wire);
        Main.AddInfo($"模拟电表：{entry.DiText} 将应答数据 {(wire.Length == 0 ? "（空）" : entry.DataText)}" +
                     (Main.Simulator is null ? "（启动模拟电表后生效）" : ""));
    }
}
