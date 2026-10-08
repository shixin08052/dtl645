using System.Text;
using System.Windows;
using System.Windows.Threading;
using Dlt645.App.Services;
using Dlt645.App.ViewModels;
using Dlt645.Core.DataItems;
using Dlt645.Core.Logging;

namespace Dlt645.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        AppPaths.Initialize();
        FileLogger.Initialize(AppPaths.DataDirectory);
        FileLogger.Info($"==== 程序启动 版本 {AppPaths.Version}，系统 {Environment.OSVersion} ====");

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            FileLogger.Error("未处理的异常", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            FileLogger.Error("未观察到的任务异常", args.Exception);
            args.SetObserved();
        };

        DataItemCatalog catalog;
        string? warning;
        try
        {
            catalog = DataItemCatalog.Load(AppPaths.ProgramDirectory, out warning);
        }
        catch (Exception ex)
        {
            FileLogger.Error("加载数据项配置失败", ex);
            MessageBox.Show($"加载数据项配置失败：{ex.Message}", "启动失败", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        IsSelfTest = e.Args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));
        AppSettings.ReadOnly = IsSelfTest;

        var settings = AppSettings.Load();
        var vm = new MainViewModel(catalog, settings, warning);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();

        if (IsSelfTest) Dispatcher.InvokeAsync(() => RunSelfTestAsync(window, vm), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>以 --selftest 启动：自动走一遍所有页面（模拟电表），退出码 0 表示通过。</summary>
    public static bool IsSelfTest { get; private set; }

    private async Task RunSelfTestAsync(MainWindow window, MainViewModel vm)
    {
        int code;
        try
        {
            FileLogger.Info("==== 自检开始 ====");
            // 按常见笔记本的小屏尺寸（1366×768 / 1920×1080@150%）测试布局
            window.WindowState = WindowState.Normal;
            window.Width = 1280;
            window.Height = 720;
            var failures = await vm.RunSelfTestAsync(async () =>
            {
                window.UpdateLayout();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            }, min => CheckResultGridHeight(window, min));
            foreach (var f in failures) FileLogger.Error("自检失败：" + f);
            code = failures.Count == 0 ? 0 : 1;
            FileLogger.Info(code == 0 ? "==== 自检通过 ====" : $"==== 自检失败（{failures.Count} 项） ====");
        }
        catch (Exception ex)
        {
            FileLogger.Error("自检异常", ex);
            code = 3;
        }
        try { window.Close(); } catch { }
        Environment.Exit(code);
    }

    /// <summary>当前显示的结果表格有数据时，高度必须足够查看和滚动。</summary>
    private static string? CheckResultGridHeight(DependencyObject root, double minHeight)
    {
        foreach (var grid in FindVisible<System.Windows.Controls.DataGrid>(root))
        {
            if (grid.Items.Count > 0 && grid.ActualHeight < minHeight)
                return $"结果表格高度只有 {grid.ActualHeight:F0} 像素（窗口 {((Window)root).ActualWidth:F0}×{((Window)root).ActualHeight:F0}），无法查看结果";
        }
        return null;
    }

    private static IEnumerable<T> FindVisible<T>(DependencyObject parent) where T : FrameworkElement
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < n; i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T t && t.IsVisible) yield return t;
            foreach (var x in FindVisible<T>(child)) yield return x;
        }
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLogger.Error("界面线程未处理的异常", e.Exception);
        if (IsSelfTest)
        {
            // 自检时界面异常立即以退出码 2 结束，避免弹窗或关闭确认导致 CI 卡住
            Environment.Exit(2);
        }
        var sb = new StringBuilder();
        sb.AppendLine("程序发生了意外错误（E999），已记录到日志。");
        sb.AppendLine();
        sb.AppendLine(e.Exception.Message);
        sb.AppendLine();
        sb.AppendLine("可以继续使用；如问题反复出现，请点击“导出诊断信息”发给工程师。");
        MessageBox.Show(sb.ToString(), "错误", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
