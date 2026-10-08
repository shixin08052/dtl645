using Dlt645.App.Infrastructure;
using Dlt645.Core.DataItems;
using Dlt645.Core.Protocol;

namespace Dlt645.App.ViewModels;

/// <summary>带勾选状态的选项（费率、月份等）。</summary>
public sealed class SelectableOption<T> : ObservableObject
{
    private bool _isChecked;

    public SelectableOption(T value, string label, bool isChecked = false)
    {
        Value = value;
        Label = label;
        _isChecked = isChecked;
    }

    public T Value { get; }
    public string Label { get; }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}

/// <summary>页面中可勾选的数据项。</summary>
public sealed class SelectableItem : ObservableObject
{
    private bool _isChecked;

    public SelectableItem(DataItemDefinition definition, bool isChecked)
    {
        Definition = definition;
        _isChecked = isChecked;
    }

    public DataItemDefinition Definition { get; }
    public string Name => Definition.Name;
    public string Group => string.IsNullOrEmpty(Definition.Group) ? "其他" : Definition.Group;

    public string Tooltip
    {
        get
        {
            var d = Definition;
            var lines = new List<string> { $"数据标识：{DataId.FormatSpaced(d.Di)}" };
            lines.Add("格式：" + string.Join(" + ", d.Fields.Select(f =>
                (string.IsNullOrEmpty(f.Name) ? "" : f.Name + " ") + f.Format.Spec + (f.Unit.Length > 0 ? $" ({f.Unit})" : ""))));
            if (d.Tariffs) lines.Add("DI1 为费率号");
            if (d.History == HistoryKind.Month) lines.Add("DI0 为结算日（00 当前，01~0C 上1~12月）");
            if (d.History == HistoryKind.Times) lines.Add($"DI0 为记录序号（01~{d.HistoryMax:X2}）");
            if (d.Note.Length > 0) lines.Add(d.Note);
            return string.Join(Environment.NewLine, lines);
        }
    }

    public bool IsChecked
    {
        get => _isChecked;
        set => SetProperty(ref _isChecked, value);
    }
}
