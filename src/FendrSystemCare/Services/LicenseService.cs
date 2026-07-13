using System.IO;
using System.Text.Json;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>API anahtarını doğrular ve %AppData% altında saklar.</summary>
public sealed class LicenseService : ILicenseService
{
    private const string ActivationUrlDefault = "https://bayram1233.github.io/keyken/";

    private readonly string _licensePath;
    private string? _storedKey;

    public bool IsLicensed => _storedKey is not null && LicenseKeyHelper.IsValid(_storedKey);
    public string? StoredKey => _storedKey;
    public string ActivationUrl { get; } = ActivationUrlDefault;

    public LicenseService()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FendrSystemCare");
        Directory.CreateDirectory(dir);
        _licensePath = Path.Combine(dir, "license.json");
        Load();
    }

    public bool ValidateAndSave(string apiKey)
    {
        if (!LicenseKeyHelper.IsValid(apiKey)) return false;
        _storedKey = apiKey.Trim().ToUpperInvariant();
        try
        {
            var json = JsonSerializer.Serialize(new { Key = _storedKey, ActivatedAt = DateTime.UtcNow });
            File.WriteAllText(_licensePath, json);
            return true;
        }
        catch { return false; }
    }

    public void Clear()
    {
        _storedKey = null;
        try { if (File.Exists(_licensePath)) File.Delete(_licensePath); } catch { }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_licensePath)) return;
            var json = File.ReadAllText(_licensePath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Key", out var key))
            {
                var k = key.GetString();
                if (LicenseKeyHelper.IsValid(k)) _storedKey = k;
            }
        }
        catch { }
    }
}
