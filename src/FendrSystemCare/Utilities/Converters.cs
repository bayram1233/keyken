using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using FendrSystemCare.Models;

namespace FendrSystemCare.Utilities;

/// <summary>Converts a byte count to a friendly size string for the UI.</summary>
public sealed class BytesToStringConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is long bytes ? FormatHelper.Bytes(bytes) : "0 B";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverts a boolean (used for enabling/disabling controls).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is bool b && !b;
}

/// <summary>Shows an element only when the bound boolean is true.</summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value is bool b && b;
        if (parameter is string s && s.Equals("invert", StringComparison.OrdinalIgnoreCase))
            visible = !visible;
        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility v && v == Visibility.Visible;
}

/// <summary>Maps an <see cref="OperationStatus"/> to a status brush.</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is OperationStatus status
            ? status switch
            {
                OperationStatus.Completed => new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
                OperationStatus.Running => new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                OperationStatus.Failed => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                OperationStatus.Cancelled => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                _ => new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93))
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Maps a startup impact level to a colour for the impact badge.</summary>
public sealed class ImpactToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StartupImpact impact
            ? impact switch
            {
                StartupImpact.High => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                StartupImpact.Medium => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                StartupImpact.Low => new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
                _ => new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93))
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Dashboard sağlık seviyesini renge çevirir.</summary>
public sealed class HealthLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is DashboardHealthLevel level
            ? level switch
            {
                DashboardHealthLevel.Excellent => new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
                DashboardHealthLevel.Good => new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF)),
                DashboardHealthLevel.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                DashboardHealthLevel.Critical => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                _ => new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93))
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Log seviyesini renge çevirir.</summary>
public sealed class LogLevelToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is LogLevel level
            ? level switch
            {
                LogLevel.Success => new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)),
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)),
                LogLevel.Error => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)),
                _ => new SolidColorBrush(Color.FromRgb(0x0A, 0x84, 0xFF))
            }
            : Brushes.Gray;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
