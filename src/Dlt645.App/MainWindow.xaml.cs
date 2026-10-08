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

        var s = vm.Settings;
        if (s.WindowWidth >= MinWidth && s.WindowWidth <= SystemParameters.VirtualScreenWidth) Width = s.WindowWidth;
        if (s.WindowHeight >= MinHeight && s.WindowHeight <= SystemParameters.VirtualScreenHeight) Height = s.WindowHeight;
        if (s.WindowMaximized) WindowState = WindowState.Maximized;

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
        if (WindowState == WindowState.Normal)
        {
            s.WindowWidth = Width;
            s.WindowHeight = Height;
        }
        _vm.SaveSettings();
        _vm.Dispose();
    }
}
