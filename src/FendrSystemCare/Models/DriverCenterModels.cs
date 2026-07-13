namespace FendrSystemCare.Models;

public enum DriverDeviceFilter
{
    All,
    Problem,
    Missing,
    Unsigned,
    Outdated,
    Disabled,
    Unknown,
    RecentlyInstalled,
    OldDrivers
}

/// <summary>Sürücü merkezi özet istatistikleri.</summary>
public sealed class DriverCenterStats
{
    public int InstalledCount { get; init; }
    public int MissingCount { get; init; }
    public int OutdatedCount { get; init; }
    public int UnsignedCount { get; init; }
    public int DisabledCount { get; init; }
    public int UnknownCount { get; init; }
    public int ProblemCount { get; init; }
    public int HealthScore { get; init; }
    public string HealthRating { get; init; } = "Bilinmiyor";
    public DateTime? LastScan { get; init; }
    public DateTime? LastBackup { get; init; }
    public DateTime? LastRestore { get; init; }
    public string BackupStatus { get; init; } = "Yedek yok";
    public IReadOnlyList<string> HealthFindings { get; init; } = Array.Empty<string>();
}

/// <summary>Tam donanım/sürücü kaydı.</summary>
public sealed class DriverDevice
{
    public required string DeviceName { get; init; }
    public string DeviceClass { get; init; } = "Bilinmiyor";
    public string Category { get; init; } = "Diğer";
    public string Manufacturer { get; init; } = "Bilinmiyor";
    public string Provider { get; init; } = "Bilinmiyor";
    public string DriverVersion { get; init; } = "Bilinmiyor";
    public DateTime? DriverDate { get; init; }
    public string HardwareId { get; init; } = string.Empty;
    public IReadOnlyList<string> HardwareIds { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> CompatibleIds { get; init; } = Array.Empty<string>();
    public string DeviceInstanceId { get; init; } = string.Empty;
    public string ClassGuid { get; init; } = string.Empty;
    public string InfName { get; init; } = string.Empty;
    public string InfPath { get; init; } = string.Empty;
    public string DriverStorePath { get; init; } = string.Empty;
    public string Service { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string Signer { get; init; } = "Bilinmiyor";
    public bool IsSigned { get; init; }
    public string Status { get; init; } = "Bilinmiyor";
    public string DeviceState { get; init; } = "Bilinmiyor";
    public int ProblemCode { get; init; }
    public bool IsEnabled { get; init; } = true;
    public bool IsMissing { get; init; }
    public bool IsOutdated { get; set; }
    public bool IsUnknown => DeviceClass is "Unknown" or "Bilinmiyor" || string.IsNullOrWhiteSpace(HardwareId);
    public string? LatestVersion { get; set; }

    public string SignatureStatus => IsSigned ? "İmzalı" : "İmzasız";
    public string ProblemText => ProblemCode == 0 ? "Yok" : $"Kod {ProblemCode}: {ProblemDescription}";

  public string ProblemDescription => ProblemCode switch
    {
        0 => "Sorun yok",
        1 => "Yanlış yapılandırılmış",
        10 => "Başlatılamıyor",
        18 => "Yeniden yüklenmeli",
        22 => "Devre dışı",
        28 => "Sürücüler yüklü değil",
        31 => "Bu cihaz çalışmıyor",
        43 => "Durduruldu",
        45 => "Bağlı değil",
        _ => "Bilinmeyen hata"
    };

    public string HardwareIdsDisplay => HardwareIds.Count > 0
        ? string.Join(Environment.NewLine, HardwareIds)
        : HardwareId;

    public bool HasProblem => ProblemCode != 0 || IsMissing || !IsEnabled;
}

/// <summary>Sürücü deposu paketi.</summary>
public sealed class DriverStorePackage
{
    public string PublishedName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ClassName { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool InUse { get; set; }
}

/// <summary>Yedekleme geçmişi kaydı.</summary>
public sealed class DriverBackupRecord
{
    public DateTime Date { get; init; }
    public string Path { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public int DriverCount { get; init; }
    public bool Verified { get; init; }
}
