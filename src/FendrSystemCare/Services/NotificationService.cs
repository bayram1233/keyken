using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.Services;

/// <summary>
/// Decoupled notification hub. Services raise notifications here; the main
/// window subscribes to <see cref="NotificationRequested"/> and renders them as
/// a Fluent snackbar plus a Windows tray balloon. Every notification is mirrored
/// to the log.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private readonly ILoggingService _log;

    /// <summary>Raised whenever a notification should be surfaced to the user.</summary>
    public event EventHandler<NotificationEventArgs>? NotificationRequested;

    public NotificationService(ILoggingService log) => _log = log;

    public void ShowInfo(string title, string message) => Raise(LogLevel.Info, title, message);
    public void ShowSuccess(string title, string message) => Raise(LogLevel.Success, title, message);
    public void ShowWarning(string title, string message) => Raise(LogLevel.Warning, title, message);
    public void ShowError(string title, string message) => Raise(LogLevel.Error, title, message);

    private void Raise(LogLevel level, string title, string message)
    {
        switch (level)
        {
            case LogLevel.Success: _log.Success(title, message); break;
            case LogLevel.Warning: _log.Warning(title, message); break;
            case LogLevel.Error: _log.Error($"{title} - {message}"); break;
            default: _log.Info(title, message); break;
        }

        NotificationRequested?.Invoke(this, new NotificationEventArgs(level, title, message));
    }
}

/// <summary>Payload describing a notification to display.</summary>
public sealed class NotificationEventArgs : EventArgs
{
    public NotificationEventArgs(LogLevel level, string title, string message)
    {
        Level = level;
        Title = title;
        Message = message;
    }

    public LogLevel Level { get; }
    public string Title { get; }
    public string Message { get; }
}
