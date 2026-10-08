using Dlt645.Core.Errors;

namespace Dlt645.App.ViewModels;

/// <summary>界面顶部的错误提示条。</summary>
public sealed class ErrorBanner
{
    public ErrorBanner(MeterException ex, bool isWarning = false)
    {
        Code = ex.Code.ToCodeString();
        Title = ex.Info.Title;
        Detail = ex.Detail ?? string.Empty;
        Causes = ex.Info.Causes.Count == 0 ? string.Empty : "• " + string.Join(Environment.NewLine + "• ", ex.Info.Causes);
        Suggestions = ex.Info.Suggestions.Count == 0 ? string.Empty : "• " + string.Join(Environment.NewLine + "• ", ex.Info.Suggestions);
        FirstSuggestion = ex.Info.Suggestions.Count == 0 ? string.Empty : "建议：" + ex.Info.Suggestions[0];
        IsWarning = isWarning;
        Time = DateTime.Now;
    }

    public string Code { get; }
    public string Title { get; }
    public string Detail { get; }
    public string Causes { get; }
    public string Suggestions { get; }
    /// <summary>折叠状态下显示的第一条建议。</summary>
    public string FirstSuggestion { get; }
    public bool IsWarning { get; }
    public DateTime Time { get; }

    public string Headline => $"[{Code}] {Title}";

    public override string ToString() =>
        $"{Time:yyyy-MM-dd HH:mm:ss} {Headline}{Environment.NewLine}" +
        (Detail.Length > 0 ? $"详情：{Detail}{Environment.NewLine}" : string.Empty) +
        $"可能原因：{Environment.NewLine}{Causes}{Environment.NewLine}排查建议：{Environment.NewLine}{Suggestions}";
}
