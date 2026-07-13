using System.IO;
using System.Text.Json;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.Services;

/// <summary>
/// Persists <see cref="AppSettings"/> as indented JSON under
/// %AppData%\FendrSystemCare\settings.json. Falls back to defaults when the
/// file is missing or corrupt.
/// </summary>
public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string _path;
    private readonly ILoggingService _log;

    public AppSettings Current { get; private set; } = new();

    public SettingsService(ILoggingService log)
    {
        _log = log;
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "FendrSystemCare");
        Directory.CreateDirectory(dir);
        _path = Path.Combine(dir, "settings.json");
    }

    public async Task LoadAsync(CancellationToken ct = default)
    {
        try
        {
            if (File.Exists(_path))
            {
                await using var stream = File.OpenRead(_path);
                var loaded = await JsonSerializer.DeserializeAsync<AppSettings>(stream, JsonOptions, ct);
                if (loaded is not null)
                    Current = loaded;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to load settings; using defaults.", ex);
            Current = new AppSettings();
        }
    }

    public async Task SaveAsync(CancellationToken ct = default)
    {
        try
        {
            await using var stream = File.Create(_path);
            await JsonSerializer.SerializeAsync(stream, Current, JsonOptions, ct);
            _log.Info("Settings saved.");
        }
        catch (Exception ex)
        {
            _log.Error("Failed to save settings.", ex);
        }
    }
}
