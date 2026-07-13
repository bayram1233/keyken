namespace FendrSystemCare.Models;

/// <summary>Özet kart durumu renkleri.</summary>
public enum DashboardHealthLevel
{
    Excellent,
    Good,
    Warning,
    Critical,
    Unknown
}

/// <summary>Üst özet kartı.</summary>
public sealed class DashboardSummaryCard
{
    public required string Key { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public required string StatusText { get; init; }
    public double Progress { get; init; }
    public DashboardHealthLevel Level { get; init; }
    public string? ActionLabel { get; init; }
}

/// <summary>Uyarı merkezi girişi.</summary>
public sealed class DashboardAlert
{
    public required string Title { get; init; }
    public required string Message { get; init; }
    public DashboardHealthLevel Severity { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.Now;
}

/// <summary>Son işlem kaydı.</summary>
public sealed class DashboardActivity
{
    public DateTime Time { get; init; }
    public required string Operation { get; init; }
    public required string Status { get; init; }
    public required string Result { get; init; }
    public LogLevel Level { get; init; }
}

/// <summary>Performans alt skorları.</summary>
public sealed class PerformanceBreakdown
{
    public int CpuScore { get; init; }
    public int MemoryScore { get; init; }
    public int StorageScore { get; init; }
    public int GraphicsScore { get; init; }
    public int WindowsScore { get; init; }
    public int OverallScore { get; init; }
    public IReadOnlyList<string> Explanations { get; init; } = Array.Empty<string>();
}

/// <summary>Sistem bilgisi detayları.</summary>
public sealed class DashboardSystemInfo
{
    public string ComputerName { get; init; } = Environment.MachineName;
    public string CurrentUser { get; init; } = Environment.UserName;
    public string WindowsEdition { get; init; } = "Bilinmiyor";
    public string WindowsBuild { get; init; } = "Bilinmiyor";
    public string WindowsVersion { get; init; } = "Bilinmiyor";
    public string InstallDate { get; init; } = "Bilinmiyor";
    public string Uptime { get; init; } = "-";
    public string Architecture { get; init; } = "Bilinmiyor";
    public string Language { get; init; } = "Bilinmiyor";
    public string Timezone { get; init; } = "Bilinmiyor";
    public string BootMode { get; init; } = "Bilinmiyor";
    public string SecureBoot { get; init; } = "Bilinmiyor";
    public string TpmVersion { get; init; } = "Bilinmiyor";
    public string BitLockerStatus { get; init; } = "Bilinmiyor";
    public string ActivationStatus { get; init; } = "Bilinmiyor";
}

/// <summary>İşlemci detayları.</summary>
public sealed class DashboardCpuInfo
{
    public string Name { get; init; } = "Bilinmiyor";
    public string Manufacturer { get; init; } = "Bilinmiyor";
    public string Socket { get; init; } = "Bilinmiyor";
    public string Architecture { get; init; } = "Bilinmiyor";
    public int Cores { get; init; }
    public int Threads { get; init; }
    public double BaseClockGhz { get; init; }
    public double CurrentClockGhz { get; init; }
    public double MaxClockGhz { get; init; }
    public double UsagePercent { get; init; }
    public string Temperature { get; init; } = "Yok";
    public string Voltage { get; init; } = "Yok";
    public string Power { get; init; } = "Yok";
    public string Cache { get; init; } = "Bilinmiyor";
}

/// <summary>Ekran kartı detayları.</summary>
public sealed class DashboardGpuInfo
{
    public string Name { get; init; } = "Bilinmiyor";
    public string Vendor { get; init; } = "Bilinmiyor";
    public string DriverVersion { get; init; } = "Bilinmiyor";
    public string DriverDate { get; init; } = "Bilinmiyor";
    public double UsagePercent { get; init; }
    public string VramUsage { get; init; } = "-";
    public string Temperature { get; init; } = "Yok";
    public string ClockSpeed { get; init; } = "Yok";
    public string Power { get; init; } = "Yok";
}

/// <summary>Bellek detayları.</summary>
public sealed class DashboardMemoryInfo
{
    public double InstalledGb { get; init; }
    public double AvailableGb { get; init; }
    public double UsedGb { get; init; }
    public double SpeedMhz { get; init; }
    public int SlotCount { get; init; }
    public string MemoryType { get; init; } = "Bilinmiyor";
    public double UsagePercent { get; init; }
}

/// <summary>Depolama sürücü detayı.</summary>
public sealed class DashboardDriveInfo
{
    public string DriveLetter { get; init; } = "-";
    public string Label { get; init; } = "-";
    public string Model { get; init; } = "Bilinmiyor";
    public string SerialNumber { get; init; } = "Bilinmiyor";
    public string FileSystem { get; init; } = "Bilinmiyor";
    public double CapacityGb { get; init; }
    public double UsedGb { get; init; }
    public double FreeGb { get; init; }
    public string Health { get; init; } = "Bilinmiyor";
    public string SmartStatus { get; init; } = "Bilinmiyor";
    public string Temperature { get; init; } = "Yok";
    public string MediaType { get; init; } = "Bilinmiyor";
    public double UsedPercent => CapacityGb <= 0 ? 0 : Math.Round(UsedGb / CapacityGb * 100, 1);
}

/// <summary>Ağ bilgisi.</summary>
public sealed class DashboardNetworkInfo
{
    public string InternetStatus { get; init; } = "Bilinmiyor";
    public string AdapterName { get; init; } = "Bilinmiyor";
    public string PublicIp { get; init; } = "-";
    public string LocalIp { get; init; } = "-";
    public string Gateway { get; init; } = "-";
    public string Dns { get; init; } = "-";
    public string MacAddress { get; init; } = "-";
    public string DownloadSpeed { get; init; } = "-";
    public string UploadSpeed { get; init; } = "-";
    public string CurrentUsage { get; init; } = "-";
}

/// <summary>Dashboard düzen tercihleri.</summary>
public sealed class DashboardLayoutSettings
{
    public bool ShowSummary { get; set; } = true;
    public bool ShowSystemInfo { get; set; } = true;
    public bool ShowHardware { get; set; } = true;
    public bool ShowStorage { get; set; } = true;
    public bool ShowNetwork { get; set; } = true;
    public bool ShowCharts { get; set; } = true;
    public bool ShowActivity { get; set; } = true;
    public bool ShowAlerts { get; set; } = true;
    public bool ShowQuickActions { get; set; } = true;
    public bool ShowPerformanceScores { get; set; } = true;
}

/// <summary>Tüm dashboard verisinin anlık görüntüsü.</summary>
public sealed class DashboardSnapshot
{
    public HealthScore Health { get; init; } = new();
    public PerformanceBreakdown Performance { get; init; } = new();
    public DashboardSystemInfo System { get; init; } = new();
    public DashboardCpuInfo Cpu { get; init; } = new();
    public DashboardGpuInfo Gpu { get; init; } = new();
    public DashboardMemoryInfo Memory { get; init; } = new();
    public DashboardNetworkInfo Network { get; init; } = new();
    public IReadOnlyList<DashboardSummaryCard> SummaryCards { get; init; } = Array.Empty<DashboardSummaryCard>();
    public IReadOnlyList<DashboardDriveInfo> Drives { get; init; } = Array.Empty<DashboardDriveInfo>();
    public IReadOnlyList<DashboardAlert> Alerts { get; init; } = Array.Empty<DashboardAlert>();
    public IReadOnlyList<DashboardActivity> Activities { get; init; } = Array.Empty<DashboardActivity>();
    public DateTime LoadedAt { get; init; } = DateTime.Now;
}
