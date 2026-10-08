using System.Windows;
using Microsoft.Win32;

namespace Dlt645.App.Services;

/// <summary>对话框封装。</summary>
public static class Dialogs
{
    public static string? SaveFile(string title, string filter, string defaultName)
    {
        var dlg = new SaveFileDialog
        {
            Title = title,
            Filter = filter,
            FileName = defaultName,
            AddExtension = true,
            OverwritePrompt = true,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        };
        return dlg.ShowDialog(Application.Current?.MainWindow) == true ? dlg.FileName : null;
    }

    public static void Info(string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, "提示", MessageBoxButton.OK, MessageBoxImage.Information);

    public static bool Confirm(string message) =>
        MessageBox.Show(Application.Current?.MainWindow!, message, "确认", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    /// <summary>导出成功后询问是否打开所在文件夹。</summary>
    public static void ExportDone(string path)
    {
        if (MessageBox.Show(Application.Current?.MainWindow!, $"已导出到：\n{path}\n\n是否打开所在文件夹？", "导出成功",
                MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
        {
            try
            {
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\"");
            }
            catch
            {
                // 打不开资源管理器不影响导出结果
            }
        }
    }
}
