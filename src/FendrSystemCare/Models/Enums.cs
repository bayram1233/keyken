namespace FendrSystemCare.Models;

/// <summary>Selectable application theme.</summary>
public enum AppTheme
{
    /// <summary>Follow the current Windows system theme.</summary>
    System,
    Light,
    Dark
}

/// <summary>Severity levels used by logging and the notification system.</summary>
public enum LogLevel
{
    Info,
    Success,
    Warning,
    Error
}

/// <summary>Overall status of a maintenance operation.</summary>
public enum OperationStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>Estimated performance impact of a startup entry.</summary>
public enum StartupImpact
{
    Unknown,
    Low,
    Medium,
    High
}

/// <summary>Where a startup entry is registered.</summary>
public enum StartupLocation
{
    CurrentUserRun,
    LocalMachineRun,
    StartupFolder
}

/// <summary>Format used when exporting a report.</summary>
public enum ReportFormat
{
    Json,
    Html,
    Pdf
}
