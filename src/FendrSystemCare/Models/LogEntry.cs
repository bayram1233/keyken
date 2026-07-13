namespace FendrSystemCare.Models;

/// <summary>A single structured log line surfaced in the UI and written to disk.</summary>
public sealed class LogEntry
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public LogLevel Level { get; init; }
    public required string Message { get; init; }
    public string? Detail { get; init; }

    public override string ToString() =>
        $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] {Level,-7} {Message}{(Detail is null ? "" : " - " + Detail)}";
}
