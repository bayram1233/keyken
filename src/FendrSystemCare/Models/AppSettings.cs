namespace FendrSystemCare.Models;

/// <summary>
/// User-configurable settings persisted as JSON under %AppData%. Mutable so the
/// settings view-model can edit it in place before saving.
/// </summary>
public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;

    /// <summary>Accent colour in "#RRGGBB" form; empty uses the system accent.</summary>
    public string AccentColor { get; set; } = "#0A84FF";

    /// <summary>UI language code (e.g. "en", "tr").</summary>
    public string Language { get; set; } = "en";

    public bool AutoUpdate { get; set; } = true;
    public bool AutoScan { get; set; }
    public bool CreateRestorePointAutomatically { get; set; } = true;
    public bool NotificationsEnabled { get; set; } = true;
    public bool MinimizeToTray { get; set; } = true;
    public bool RunMonitoringInBackground { get; set; } = true;
}
