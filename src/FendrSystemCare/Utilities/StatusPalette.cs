using System.Windows.Media;

namespace FendrSystemCare.Utilities;

/// <summary>
/// Frozen status brushes mirroring the accent colours defined in Colors.xaml.
/// Used by dashboard view-models that need to assign a status colour in code
/// (cards, alerts, performance meters) without coupling to XAML resources.
/// </summary>
public static class StatusPalette
{
    public static readonly Brush Success = Freeze("#30D158");
    public static readonly Brush Warning = Freeze("#FF9F0A");
    public static readonly Brush Danger = Freeze("#FF453A");
    public static readonly Brush Info = Freeze("#0A84FF");
    public static readonly Brush Muted = Freeze("#8E8E93");

    /// <summary>Maps a 0-100 score to green/amber/red.</summary>
    public static Brush ForScore(double score) => score switch
    {
        >= 80 => Success,
        >= 50 => Warning,
        _ => Danger
    };

    /// <summary>Maps a usage percentage (higher = worse) to a status colour.</summary>
    public static Brush ForUsage(double percent) => percent switch
    {
        >= 90 => Danger,
        >= 75 => Warning,
        _ => Success
    };

    /// <summary>Maps a temperature in °C to a status colour.</summary>
    public static Brush ForTemperature(double? celsius) => celsius switch
    {
        null => Muted,
        >= 85 => Danger,
        >= 70 => Warning,
        _ => Success
    };

    private static Brush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        brush.Freeze();
        return brush;
    }
}
