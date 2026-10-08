using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Dlt645.App.ViewModels;
using Dlt645.Core.Communication;

namespace Dlt645.App.Infrastructure;

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}

/// <summary>null / 空字符串 → Collapsed。</summary>
public sealed class NotEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture) =>
        value is null || (value is string s && s.Length == 0) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

public sealed class ConnectionStateToBrushConverter : IValueConverter
{
    /// <summary>目标类型为 Color 时（如阴影效果）返回颜色，否则返回画刷。</summary>
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var brush = value switch
        {
            ConnectionState.PortOpen => Brush("#1E88E5"),
            ConnectionState.CommOk => Brush("#2E7D32"),
            ConnectionState.Timeout => Brush("#EF6C00"),
            ConnectionState.Fault => Brush("#C62828"),
            _ => Brush("#9E9E9E"),
        };
        return targetType == typeof(Color) ? brush.Color : brush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    internal static SolidColorBrush Brush(string hex)
    {
        var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        b.Freeze();
        return b;
    }
}

/// <summary>报文类型 → 颜色。parameter="text" 时返回文字颜色，否则返回标签背景色。</summary>
public sealed class MonitorKindToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool text = parameter as string == "text";
        return value switch
        {
            MonitorKind.Tx => ConnectionStateToBrushConverter.Brush(text ? "#0D47A1" : "#1E88E5"),
            MonitorKind.Rx => ConnectionStateToBrushConverter.Brush(text ? "#1B5E20" : "#2E7D32"),
            MonitorKind.Echo => ConnectionStateToBrushConverter.Brush(text ? "#9E9E9E" : "#BDBDBD"),
            MonitorKind.Warning => ConnectionStateToBrushConverter.Brush(text ? "#E65100" : "#F57C00"),
            MonitorKind.Error => ConnectionStateToBrushConverter.Brush(text ? "#B71C1C" : "#D32F2F"),
            _ => ConnectionStateToBrushConverter.Brush(text ? "#455A64" : "#78909C"),
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
