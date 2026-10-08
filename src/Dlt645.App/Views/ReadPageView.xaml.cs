using System.Windows;
using System.Windows.Controls;

namespace Dlt645.App.Views;

public partial class ReadPageView : UserControl
{
    /// <summary>无论选择区多大，结果表格至少保留的高度。</summary>
    private const double MinResultsHeight = 150;

    public ReadPageView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateSelectionMaxHeight();
    }

    private void Root_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateSelectionMaxHeight();

    /// <summary>
    /// 选择区最大高度 = 页面高度 − 标题 − 操作栏 − 结果表格保留高度（且不超过 290），
    /// 小屏幕上选择区自动变矮并出现滚动条，而不是把结果表格挤没。
    /// </summary>
    private void UpdateSelectionMaxHeight()
    {
        if (ActualHeight <= 0) return;
        double available = ActualHeight - HeaderPanel.ActualHeight - ActionBar.ActualHeight - 30 - MinResultsHeight;
        SelectionScroll.MaxHeight = Math.Clamp(available, 70, 290);
    }
}
