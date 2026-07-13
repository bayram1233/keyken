namespace FendrSystemCare.Models;

/// <summary>
/// Immutable snapshot of the machine's hardware and Windows configuration,
/// populated once by <c>ISystemInfoService</c> and shown on the dashboard.
/// </summary>
public sealed class SystemInformation
{
    public string CpuName { get; init; } = "Unknown";
    public int CpuCores { get; init; }
    public int CpuLogicalProcessors { get; init; }
    public double CpuMaxClockGhz { get; init; }

    public string GpuName { get; init; } = "Unknown";
    public double GpuMemoryGb { get; init; }
    public string GpuDriverVersion { get; init; } = "Unknown";

    public double TotalRamGb { get; init; }
    public string RamType { get; init; } = "Unknown";
    public double RamSpeedMhz { get; init; }

    public string WindowsEdition { get; init; } = "Unknown";
    public string WindowsVersion { get; init; } = "Unknown";
    public string WindowsBuild { get; init; } = "Unknown";
    public bool IsWindows11 { get; init; }

    public string BiosVersion { get; init; } = "Unknown";
    public string BiosDate { get; init; } = "Unknown";

    public string MotherboardManufacturer { get; init; } = "Unknown";
    public string MotherboardProduct { get; init; } = "Unknown";

    public string MachineName { get; init; } = Environment.MachineName;
    public bool IsActivated { get; init; }
    public string ActivationStatus { get; init; } = "Unknown";

    /// <summary>Per-physical-disk health details.</summary>
    public IReadOnlyList<DiskHealth> Disks { get; init; } = Array.Empty<DiskHealth>();
}

/// <summary>Health and capacity details for a single physical disk.</summary>
public sealed class DiskHealth
{
    public string Model { get; init; } = "Unknown";
    public string MediaType { get; init; } = "Unknown";
    public double SizeGb { get; init; }
    public string HealthStatus { get; init; } = "Unknown";
    public int? TemperatureCelsius { get; init; }
}

/// <summary>Represents a single logical volume shown on the dashboard.</summary>
public sealed class StorageVolume
{
    public string Drive { get; init; } = "C:";
    public string Label { get; init; } = string.Empty;
    public double TotalGb { get; init; }
    public double FreeGb { get; init; }
    public double UsedGb => TotalGb - FreeGb;
    public double UsedPercent => TotalGb <= 0 ? 0 : Math.Round(UsedGb / TotalGb * 100, 1);
}

/// <summary>Aggregated 0-100 health score with a human-readable rating.</summary>
public sealed class HealthScore
{
    public int Score { get; init; }
    public string Rating { get; init; } = "Unknown";
    public IReadOnlyList<string> Findings { get; init; } = Array.Empty<string>();
}
