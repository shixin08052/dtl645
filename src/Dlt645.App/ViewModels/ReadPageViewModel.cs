using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Dlt645.App.Infrastructure;
using Dlt645.Core.DataItems;

namespace Dlt645.App.ViewModels;

/// <summary>页面配置。</summary>
public sealed class ReadPageOptions
{
    public required string Title { get; init; }
    public required string Glyph { get; init; }
    public required string Description { get; init; }
    public required IReadOnlyList<DataItemDefinition> Items { get; init; }
    /// <summary>“一键读取常用数据”页：按配置表 commonTariffs 读取当前值，不显示选择器。</summary>
    public bool IsCommonPage { get; init; }
    public bool SupportsAutoRefresh { get; init; }
    /// <summary>默认勾选的数据项；为空时勾选配置表中标为 common 的项（没有则勾选第一项）。</summary>
    public Func<DataItemDefinition, bool>? DefaultChecked { get; init; }
}

/// <summary>
/// 通用的数据读取页：数据项勾选 + 费率选择 + 月份选择器 / 最近 N 次 + 结果表格。
/// 电能量、最大需量、实时变量、电表参数、事件记录、冻结数据、常用数据均使用本类。
/// </summary>
public sealed class ReadPageViewModel : PageViewModel
{
    private static readonly int[] TimesChoices = { 1, 2, 3, 5, 10, 12, 24, 31, 62 };
    private readonly DispatcherTimer _refreshTimer;
    private int _selectedTimes = 1;
    private bool _isAutoRefresh;
    private int _refreshIntervalSeconds;
    private string _selectionSummary = string.Empty;
    private bool _isSelectorExpanded = true;

    public ReadPageViewModel(MainViewModel main, ReadPageOptions options)
        : base(main, options.Title, options.Glyph, options.Description)
    {
        IsCommonPage = options.IsCommonPage;
        SupportsAutoRefresh = options.SupportsAutoRefresh;

        var isDefault = options.DefaultChecked ?? (options.Items.Any(i => i.Common) ? i => i.Common : null);
        for (int k = 0; k < options.Items.Count; k++)
        {
            var def = options.Items[k];
            bool check = isDefault?.Invoke(def) ?? k == 0;
            if (IsCommonPage) check = true;
            var item = new SelectableItem(def, check);
            item.PropertyChanged += OnSelectionChanged;
            Items.Add(item);
        }
        ItemsView = CollectionViewSource.GetDefaultView(Items);
        ItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SelectableItem.Group)));

        // 费率
        ShowTariffs = !IsCommonPage && options.Items.Any(i => i.Tariffs);
        for (int t = 0; t < Tariffs.Names.Count; t++)
        {
            var o = new SelectableOption<int>(t, Tariffs.Names[t], t == 0);
            o.PropertyChanged += OnSelectionChanged;
            TariffOptions.Add(o);
        }

        // 月份 / 结算日
        var monthItem = options.Items.FirstOrDefault(i => i.History == HistoryKind.Month);
        ShowMonths = !IsCommonPage && monthItem != null;
        if (monthItem != null)
        {
            for (int m = 0; m <= 12; m++)
            {
                var o = new SelectableOption<int>(m, monthItem.FormatHistory(m), m == 0);
                o.PropertyChanged += OnSelectionChanged;
                MonthOptions.Add(o);
            }
        }

        // 上 N 次
        var timesItems = options.Items.Where(i => i.History == HistoryKind.Times).ToList();
        ShowTimes = !IsCommonPage && timesItems.Count > 0;
        if (timesItems.Count > 0)
        {
            int max = timesItems.Max(i => i.HistoryMax);
            foreach (var n in TimesChoices.Where(n => n <= max)) TimesOptions.Add(n);
            if (!TimesOptions.Contains(max) && max <= 62) TimesOptions.Add(max);
        }

        RefreshIntervalOptions = new[] { 1, 2, 3, 5, 10, 30, 60 };
        _refreshIntervalSeconds = RefreshIntervalOptions.Contains(main.Settings.RefreshIntervalSeconds) ? main.Settings.RefreshIntervalSeconds : 3;
        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_refreshIntervalSeconds) };
        _refreshTimer.Tick += OnRefreshTick;

        ReadCommand = new AsyncRelayCommand(() => Main.ReadPageAsync(this), () => !Main.IsBusy);
        SelectAllCommand = new RelayCommand(() => SetAll(Items, true));
        SelectNoneCommand = new RelayCommand(() => SetAll(Items, false));
        MonthsAllCommand = new RelayCommand(() => SetAll(MonthOptions, true));
        MonthsCurrentCommand = new RelayCommand(() => { foreach (var m in MonthOptions) m.IsChecked = m.Value == 0; });
        MonthsNoneCommand = new RelayCommand(() => SetAll(MonthOptions, false));
        TariffsAllCommand = new RelayCommand(() => SetAll(TariffOptions, true));

        UpdateSummary();
    }

    public bool IsCommonPage { get; }
    public bool SupportsAutoRefresh { get; }

    public ObservableCollection<SelectableItem> Items { get; } = new();
    public ICollectionView ItemsView { get; }

    public bool ShowTariffs { get; }
    public ObservableCollection<SelectableOption<int>> TariffOptions { get; } = new();

    public bool ShowMonths { get; }
    public ObservableCollection<SelectableOption<int>> MonthOptions { get; } = new();

    public bool ShowTimes { get; }
    public ObservableCollection<int> TimesOptions { get; } = new();

    public int SelectedTimes
    {
        get => _selectedTimes;
        set
        {
            if (SetProperty(ref _selectedTimes, value)) UpdateSummary();
        }
    }

    public IReadOnlyList<int> RefreshIntervalOptions { get; }

    public int RefreshIntervalSeconds
    {
        get => _refreshIntervalSeconds;
        set
        {
            if (!SetProperty(ref _refreshIntervalSeconds, value)) return;
            _refreshTimer.Interval = TimeSpan.FromSeconds(Math.Max(1, value));
            Main.Settings.RefreshIntervalSeconds = value;
        }
    }

    public bool IsAutoRefresh
    {
        get => _isAutoRefresh;
        set
        {
            if (!SetProperty(ref _isAutoRefresh, value)) return;
            if (value)
            {
                _refreshTimer.Start();
                Main.AddInfo($"【{Title}】已开启自动刷新，间隔 {RefreshIntervalSeconds} 秒");
                if (!Main.IsBusy) ReadCommand.Execute(null);
            }
            else
            {
                _refreshTimer.Stop();
                Main.AddInfo($"【{Title}】已关闭自动刷新");
            }
        }
    }

    /// <summary>选择区是否展开（开始读取后自动收起，把空间留给结果表格）。</summary>
    public bool IsSelectorExpanded
    {
        get => _isSelectorExpanded;
        set => SetProperty(ref _isSelectorExpanded, value);
    }

    public string SelectionSummary
    {
        get => _selectionSummary;
        private set => SetProperty(ref _selectionSummary, value);
    }

    public override ICommand ReadCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand SelectNoneCommand { get; }
    public ICommand MonthsAllCommand { get; }
    public ICommand MonthsCurrentCommand { get; }
    public ICommand MonthsNoneCommand { get; }
    public ICommand TariffsAllCommand { get; }

    /// <summary>根据当前勾选生成读取请求列表。</summary>
    public IReadOnlyList<ReadRequest> BuildRequests()
    {
        var items = Items.Where(i => i.IsChecked).Select(i => i.Definition).ToList();
        if (IsCommonPage)
        {
            var list = new List<ReadRequest>();
            foreach (var item in items)
            {
                int history = item.History switch
                {
                    HistoryKind.Month => 0,
                    HistoryKind.Times => 1,
                    _ => -1,
                };
                if (item.Tariffs)
                    list.AddRange(item.CommonTariffs.Select(t => new ReadRequest(item, t, history)));
                else
                    list.Add(new ReadRequest(item, -1, history));
            }
            return list;
        }

        var tariffs = TariffOptions.Where(o => o.IsChecked).Select(o => o.Value).ToList();
        var months = MonthOptions.Where(o => o.IsChecked).Select(o => o.Value).ToList();
        return ReadRequest.Expand(items, tariffs, months, SelectedTimes).ToList();
    }

    /// <summary>检查勾选是否完整，返回提示文字；完整时返回 null。</summary>
    public string? ValidateSelection()
    {
        var items = Items.Where(i => i.IsChecked).Select(i => i.Definition).ToList();
        if (items.Count == 0) return "请至少勾选一个数据项。";
        if (ShowTariffs && items.Any(i => i.Tariffs) && !TariffOptions.Any(o => o.IsChecked)) return "请至少勾选一个费率（总/尖/峰/平/谷）。";
        if (ShowMonths && items.Any(i => i.History == HistoryKind.Month) && !MonthOptions.Any(o => o.IsChecked)) return "请至少选择一个月份（当前 / 上N月）。";
        return null;
    }

    public void StopAutoRefresh()
    {
        if (IsAutoRefresh) IsAutoRefresh = false;
    }

    public override void OnDeactivated() => StopAutoRefresh();

    private void OnRefreshTick(object? sender, EventArgs e)
    {
        if (!Main.IsConnected)
        {
            StopAutoRefresh();
            return;
        }
        if (!Main.IsBusy) ReadCommand.Execute(null);
    }

    private void OnSelectionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == "IsChecked") UpdateSummary();
    }

    private void UpdateSummary()
    {
        int items = Items.Count(i => i.IsChecked);
        int reads = BuildRequests().Count;
        SelectionSummary = items == 0 ? "未勾选数据项" : $"已选 {items} 个数据项，共需通讯 {reads} 次";
    }

    private static void SetAll(IEnumerable<SelectableItem> list, bool value)
    {
        foreach (var i in list) i.IsChecked = value;
    }

    private static void SetAll(IEnumerable<SelectableOption<int>> list, bool value)
    {
        foreach (var i in list) i.IsChecked = value;
    }
}
