using System.Collections.ObjectModel;
using System.Windows.Media;
using FendrSystemCare.Models;

namespace FendrSystemCare.Services.Interfaces;

/// <summary>Central structured logger. Every operation is recorded here.</summary>
public interface ILoggingService
{
    /// <summary>Live, UI-bindable view of recent log entries (newest first).</summary>
    ReadOnlyObservableCollection<LogEntry> Entries { get; }

    void Info(string message, string? detail = null);
    void Success(string message, string? detail = null);
    void Warning(string message, string? detail = null);
    void Error(string message, Exception? exception = null);

    /// <summary>
    /// Begins a timed operation. Dispose the returned scope to record its
    /// duration; call <see cref="IOperationScope.Complete"/> or
    /// <see cref="IOperationScope.Fail"/> to record the outcome.
    /// </summary>
    IOperationScope BeginOperation(string operation, string? detail = null);

    /// <summary>Absolute path of the on-disk log file for the current session.</summary>
    string LogFilePath { get; }
}

/// <summary>
/// A logged unit of work that records start time, duration and outcome. If it is
/// disposed without an explicit result it is logged as an incomplete warning.
/// </summary>
public interface IOperationScope : IDisposable
{
    /// <summary>Marks the operation as succeeded and logs its duration.</summary>
    void Complete(string? detail = null);

    /// <summary>Marks the operation as failed and logs the duration and error.</summary>
    void Fail(string message, Exception? exception = null);
}

/// <summary>API anahtarı doğrulama ve kalıcı lisans yönetimi.</summary>
public interface ILicenseService
{
    bool IsLicensed { get; }
    string? StoredKey { get; }
    bool ValidateAndSave(string apiKey);
    void Clear();
    string ActivationUrl { get; }
}

/// <summary>Loads and persists <see cref="AppSettings"/> to disk.</summary>
public interface ISettingsService
{
    AppSettings Current { get; }
    Task LoadAsync(CancellationToken ct = default);
    Task SaveAsync(CancellationToken ct = default);
}

/// <summary>Applies the visual theme and accent colour at runtime.</summary>
public interface IThemeService
{
    void Apply(AppTheme theme, string accentColor);
    AppTheme CurrentTheme { get; }
}

/// <summary>Shows in-app toasts/snackbars and Windows tray notifications.</summary>
public interface INotificationService
{
    void ShowInfo(string title, string message);
    void ShowSuccess(string title, string message);
    void ShowWarning(string title, string message);
    void ShowError(string title, string message);
}

/// <summary>Owns navigation between pages inside the shell.</summary>
public interface INavigationService
{
    event EventHandler<Type>? Navigated;
    void NavigateTo(Type viewModelType);
    void NavigateTo<TViewModel>() where TViewModel : class;
}

/// <summary>Creates and manages System Restore points.</summary>
public interface IRestorePointService
{
    /// <summary>Creates a restore point; returns true on success.</summary>
    Task<bool> CreateAsync(string description, CancellationToken ct = default);

    /// <summary>Lists existing restore points (sequence number + description + date).</summary>
    Task<IReadOnlyList<(int Sequence, string Description, DateTime Created)>> ListAsync(CancellationToken ct = default);

    /// <summary>Opens the built-in System Restore wizard for a specific point.</summary>
    Task RestoreAsync(int sequence, CancellationToken ct = default);
}
