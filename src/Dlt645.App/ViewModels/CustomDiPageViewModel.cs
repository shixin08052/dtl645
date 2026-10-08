using System.Windows.Input;
using Dlt645.App.Infrastructure;
using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;

namespace Dlt645.App.ViewModels;

/// <summary>自定义数据标识：手动输入 4 字节 DI，显示原始数据并尝试解析。</summary>
public sealed class CustomDiPageViewModel : PageViewModel
{
    public const string AutoFormat = "自动（按配置表 / 尝试解析）";

    private string _diText = "00010000";
    private string _matchText = string.Empty;
    private bool _isMatched;
    private string? _diError;
    private string _selectedFormat = AutoFormat;
    private bool _signed;
    private string _lastRawText = string.Empty;

    public CustomDiPageViewModel(MainViewModel main)
        : base(main, "自定义数据标识", "", "手动输入 4 字节数据标识（DI3 DI2 DI1 DI0，如 00010000），读取后显示原始数据和尝试解析的结果。结果会累积在下表中。")
    {
        ReadCommand = new AsyncRelayCommand(() => Main.ReadCustomAsync(this), () => !Main.IsBusy);
        UpdateMatch();
    }

    public override ICommand ReadCommand { get; }

    public IReadOnlyList<string> FormatOptions { get; } = new[]
    {
        AutoFormat, "XXXXXX.XX", "XXX.X", "XXX.XXX", "XX.XXXX", "X.XXX", "XX.XX", "NN", "NNNNNN", "XXXXXXXX",
        "NNNNNNNNNNNN", "YYMMDDhhmmss", "YYMMDDhhmm", "YYMMDDWW", "hhmmss", "ASCII", "HEX",
    };

    public string DiText
    {
        get => _diText;
        set
        {
            if (SetProperty(ref _diText, value)) UpdateMatch();
        }
    }

    public string? DiError
    {
        get => _diError;
        private set => SetProperty(ref _diError, value);
    }

    public string MatchText
    {
        get => _matchText;
        private set => SetProperty(ref _matchText, value);
    }

    public bool IsMatched
    {
        get => _isMatched;
        private set => SetProperty(ref _isMatched, value);
    }

    public string SelectedFormat
    {
        get => _selectedFormat;
        set => SetProperty(ref _selectedFormat, value);
    }

    public bool Signed
    {
        get => _signed;
        set => SetProperty(ref _signed, value);
    }

    public string LastRawText
    {
        get => _lastRawText;
        set => SetProperty(ref _lastRawText, value);
    }

    public bool TryGetDi(out uint di)
    {
        bool ok = DataId.TryParse(DiText, out di, out var err);
        DiError = ok ? null : err;
        return ok;
    }

    /// <summary>按所选格式解析；返回解析结果和用于显示的数据项名称。</summary>
    public (IReadOnlyList<DecodedField> Fields, string Name) Interpret(uint di, byte[] data, DataItemCatalog catalog)
    {
        var match = catalog.Find(di);
        string name = match?.Describe() ?? $"自定义 {DataId.Format(di)}";
        if (SelectedFormat == AutoFormat)
            return (match != null ? DataItemDecoder.Decode(match.Item, data) : DataItemDecoder.AutoInterpret(data), name);

        string spec = SelectedFormat switch
        {
            "ASCII" => $"ASCII:{Math.Max(1, data.Length)}",
            "HEX" => $"HEX:{Math.Max(1, data.Length)}",
            _ => SelectedFormat,
        };
        return (DataItemDecoder.DecodeWithFormat(spec, Signed, data), name + $"（按 {SelectedFormat} 解析）");
    }

    private void UpdateMatch()
    {
        if (!DataId.TryParse(DiText, out var di, out var err))
        {
            DiError = err;
            MatchText = string.Empty;
            IsMatched = false;
            return;
        }
        DiError = null;
        var m = Main.Catalog.Find(di);
        IsMatched = m != null;
        MatchText = m != null
            ? $"配置表匹配：{m.Describe()}（{string.Join(" + ", m.Item.Fields.Select(f => f.Format.Spec))}）"
            : "配置表中没有此数据项，读取后将显示原始数据并尝试按 BCD / ASCII 解析";
    }
}
