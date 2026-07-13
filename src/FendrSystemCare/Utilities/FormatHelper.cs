namespace FendrSystemCare.Utilities;

/// <summary>Formatting helpers shared across view-models.</summary>
public static class FormatHelper
{
    private static readonly string[] SizeUnits = { "B", "KB", "MB", "GB", "TB", "PB" };

    /// <summary>Converts a byte count into a human-readable string (e.g. "1.4 GB").</summary>
    public static string Bytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        var order = (int)Math.Floor(Math.Log(bytes, 1024));
        order = Math.Min(order, SizeUnits.Length - 1);
        var value = bytes / Math.Pow(1024, order);
        return $"{value:0.##} {SizeUnits[order]}";
    }

    /// <summary>Formats a <see cref="TimeSpan"/> as "Xd Yh Zm".</summary>
    public static string Uptime(TimeSpan span) =>
        $"{(int)span.TotalDays}d {span.Hours}h {span.Minutes}m";
}
