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

        var settings = AppSettings.Load();
        var vm = new MainViewModel(catalog, settings, warning);
        var window = new MainWindow(vm);
        MainWindow = window;
        window.Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        FileLogger.Error("界面线程未处理的异常", e.Exception);
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
