using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Dlt645.App.ViewModels;
using Dlt645.Core.Communication;

namespace Dlt645.App;

public partial class MainWindow : Window
{
    private const int WmDeviceChange = 0x0219;
    private const int DbtDeviceArrival = 0x8000;
    private const int DbtDeviceRemoveComplete = 0x8004;
    private const int DbtDevNodesChanged = 0x0007;

    private readonly MainViewModel _vm;

    public MainWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;

        // 窗口不能超出屏幕可用区域（不含任务栏），否则标题栏和关闭按钮会跑到屏幕外。
        // 屏幕放不下默认尺寸（如 1366×768 笔记本）时直接最大化。
        var s = vm.Settings;
        var work = SystemParameters.WorkArea;
        MinWidth = Math.Min(MinWidth, work.Width);
        MinHeight = Math.Min(MinHeight, work.Height);
        double w = s.WindowWidth >= MinWidth ? s.WindowWidth : Width;
        double h = s.WindowHeight >= MinHeight ? s.WindowHeight : Height;
        bool tooBig = w >= work.Width || h >= work.Height;
        Width = Math.Min(w, work.Width);
        Height = Math.Min(h, work.Height);
        if (s.WindowMaximized || tooBig) WindowState = WindowState.Maximized;
        if (s.MonitorHeight >= 90 && s.MonitorHeight <= 1000) MonitorRow.Height = new GridLength(s.MonitorHeight);
        Loaded += (_, _) => ApplyAutoLayout();

        // 订阅 ListBox 自己的 Items（而不是 ViewModel 的集合），保证列表已处理完新增项后再滚动；
        // 并推迟到后台优先级执行，避免在集合变更通知过程中触发布局导致
        // “ItemsControl is inconsistent with its items source” 异常
        ((INotifyCollectionChanged)MonitorList.Items).CollectionChanged += OnMonitorChanged;
        Closing += OnClosing;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        // 监听 USB 插拔（WM_DEVICECHANGE），自动刷新串口列表
        if (PresentationSource.FromVisual(this) is HwndSource source) source.AddHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmDeviceChange)
        {
            int evt = wParam.ToInt32();
            if (evt is DbtDeviceArrival or DbtDeviceRemoveComplete or DbtDevNodesChanged) _vm.NotifyDeviceChanged();
        }
        return IntPtr.Zero;
    }

    private double _expandedMonitorHeight = 160;
    private bool _applyingLayout;

    /// <summary>窗口较矮时节省空间：报文窗口（未手动设置过时）自动收起，错误详情默认折叠。</summary>
    public void ApplyAutoLayout()
    {
        bool shortWindow = ActualHeight > 0 && ActualHeight < 800;
        SetMonitorCollapsed(_vm.Settings.MonitorCollapsed ?? shortWindow, userChoice: false);
        _vm.ErrorDetailsExpanded = ActualHeight >= 850;
    }

    /// <summary>收起 / 展开报文窗口。userChoice 为 true 时记住用户的选择。</summary>
    public void SetMonitorCollapsed(bool collapsed, bool userChoice = false)
    {
        _applyingLayout = !userChoice;
        try
        {
            if (MonitorToggle.IsChecked == collapsed) ApplyMonitorState(collapsed);
            else MonitorToggle.IsChecked = collapsed;
        }
        finally
        {
            _applyingLayout = false;
        }
    }

    /// <summary>收起 / 展开报文监视窗口。</summary>
    private void MonitorToggle_Changed(object sender, RoutedEventArgs e)
    {
        bool collapsed = MonitorToggle.IsChecked == true;
        ApplyMonitorState(collapsed);
        if (!_applyingLayout) _vm.Settings.MonitorCollapsed = collapsed;
    }

    private void ApplyMonitorState(bool collapsed)
    {
        if (collapsed)
        {
            if (MonitorRow.ActualHeight >= 90) _expandedMonitorHeight = MonitorRow.ActualHeight;
            else if (MonitorRow.Height.IsAbsolute) _expandedMonitorHeight = MonitorRow.Height.Value;
            MonitorList.Visibility = Visibility.Collapsed;
            MonitorSplitter.Visibility = Visibility.Collapsed;
            MonitorRow.MinHeight = 0;
            MonitorRow.Height = GridLength.Auto;
        }
        else
        {
            MonitorList.Visibility = Visibility.Visible;
            MonitorSplitter.Visibility = Visibility.Visible;
            MonitorRow.MinHeight = 90;
            MonitorRow.Height = new GridLength(Math.Max(90, _expandedMonitorHeight));
        }
    }

    private void AddressBox_LostFocus(object sender, RoutedEventArgs e) => _vm.NormalizeAddress();

    private void OnMonitorChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || !_vm.AutoScrollMonitor || _scrollPending) return;
        _scrollPending = true;
        Dispatcher.BeginInvoke(() =>
        {
            _scrollPending = false;
            // 用户正在多选复制时不打断
            if (MonitorList.Items.Count == 0 || MonitorList.SelectedItems.Count > 1) return;
            MonitorList.ScrollIntoView(MonitorList.Items[^1]);
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool _scrollPending;

    private void MonitorCopy_Executed(object sender, ExecutedRoutedEventArgs e)
    {
        var lines = MonitorList.SelectedItems.Cast<MonitorEntry>()
            .OrderBy(x => _vm.MonitorEntries.IndexOf(x))
            .Select(x => x.ToString());
        try
        {
            Clipboard.SetText(string.Join(Environment.NewLine, lines));
        }
        catch
        {
            // 剪贴板被其他程序占用时忽略
        }
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_vm.IsBusy && !App.IsSelfTest && MessageBox.Show(this, "正在读取数据，确定要退出吗？", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        var s = _vm.Settings;
        s.WindowMaximized = WindowState == WindowState.Maximized;
        s.MonitorHeight = MonitorToggle.IsChecked == true ? _expandedMonitorHeight : MonitorRow.ActualHeight;
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        _vm.SaveSettings();
        _vm.Dispose();
    }
}
