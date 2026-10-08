using System.Collections.ObjectModel;
using System.Windows.Input;
using Dlt645.App.Infrastructure;
using Dlt645.Core.Communication;

namespace Dlt645.App.ViewModels;

/// <summary>左侧菜单中的一个功能页。</summary>
public abstract class PageViewModel : ObservableObject
{
    protected PageViewModel(MainViewModel main, string title, string glyph, string description)
    {
        Main = main;
        Title = title;
        Glyph = glyph;
        Description = description;
        ClearResultsCommand = new RelayCommand(() => Results.Clear(), () => Results.Count > 0 && !Main.IsBusy);
    }

    public MainViewModel Main { get; }
    public string Title { get; }
    /// <summary>Segoe MDL2 Assets 图标字符。</summary>
    public string Glyph { get; }
    public string Description { get; }

    public ObservableCollection<ReadResultRow> Results { get; } = new();

    /// <summary>最近一次读取所用的表号（导出时写入文件）。</summary>
    public string? LastMeterNo { get; set; }

    /// <summary>最近一次读取的时间。</summary>
    public DateTime? LastReadTime { get; set; }

    public abstract ICommand ReadCommand { get; }

    public ICommand ClearResultsCommand { get; }

    public void AddRows(IEnumerable<ReadResultRow> rows)
    {
        foreach (var r in rows)
        {
            r.Index = Results.Count + 1;
            Results.Add(r);
        }
    }

    /// <summary>页面被切走或串口断开时调用。</summary>
    public virtual void OnDeactivated()
    {
    }
}
