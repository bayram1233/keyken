using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.Services;

/// <summary>
/// Thread-safe logger that keeps an in-memory, UI-bindable ring buffer of the
/// most recent entries and appends every entry to a per-session file under
/// %LocalAppData%\FendrSystemCare\Logs.
/// </summary>
public sealed class LoggingService : ILoggingService
{
    private const int MaxInMemory = 500;

    private readonly ObservableCollection<LogEntry> _entries = new();
    private readonly object _fileLock = new();

    public ReadOnlyObservableCollection<LogEntry> Entries { get; }
    public string LogFilePath { get; }

    public LoggingService()
    {
        Entries = new ReadOnlyObservableCollection<LogEntry>(_entries);

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FendrSystemCare", "Logs");
        Directory.CreateDirectory(dir);
        LogFilePath = Path.Combine(dir, $"session-{DateTime.Now:yyyyMMdd-HHmmss}.log");
    }

    public void Info(string message, string? detail = null) => Write(LogLevel.Info, message, detail);
    public void Success(string message, string? detail = null) => Write(LogLevel.Success, message, detail);
    public void Warning(string message, string? detail = null) => Write(LogLevel.Warning, message, detail);

    public void Error(string message, Exception? exception = null) =>
        Write(LogLevel.Error, message, exception?.ToString());

    public IOperationScope BeginOperation(string operation, string? detail = null)
    {
        Info($"▶ {operation}", detail);
        return new OperationScope(this, operation);
    }

    /// <summary>Tracks a single timed operation and logs its outcome/duration.</summary>
    private sealed class OperationScope : IOperationScope
    {
        private readonly LoggingService _log;
        private readonly string _operation;
        private readonly System.Diagnostics.Stopwatch _stopwatch = System.Diagnostics.Stopwatch.StartNew();
        private bool _resolved;

        public OperationScope(LoggingService log, string operation)
        {
            _log = log;
            _operation = operation;
        }

        public void Complete(string? detail = null)
        {
            if (_resolved) return;
            _resolved = true;
            _stopwatch.Stop();
            _log.Success($"✔ {_operation} ({Elapsed()})", detail);
        }

        public void Fail(string message, Exception? exception = null)
        {
            if (_resolved) return;
            _resolved = true;
            _stopwatch.Stop();
            _log.Error($"✖ {_operation} failed after {Elapsed()}: {message}", exception);
        }

        public void Dispose()
        {
            if (_resolved) return;
            _resolved = true;
            _stopwatch.Stop();
            _log.Warning($"■ {_operation} ended without an explicit result ({Elapsed()}).");
        }

        private string Elapsed() =>
            _stopwatch.Elapsed.TotalSeconds < 1
                ? $"{_stopwatch.ElapsedMilliseconds} ms"
                : $"{_stopwatch.Elapsed.TotalSeconds:0.0} s";
    }

    private void Write(LogLevel level, string message, string? detail)
    {
        var entry = new LogEntry { Level = level, Message = message, Detail = detail };

        // File writes are synchronised; UI updates are marshalled to the dispatcher.
        lock (_fileLock)
        {
            try { File.AppendAllText(LogFilePath, entry + Environment.NewLine); }
            catch { /* Logging must never throw. */ }
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
            AddToMemory(entry);
        else
            dispatcher.BeginInvoke(() => AddToMemory(entry));
    }

    private void AddToMemory(LogEntry entry)
    {
        _entries.Insert(0, entry);
        while (_entries.Count > MaxInMemory)
            _entries.RemoveAt(_entries.Count - 1);
    }
}
