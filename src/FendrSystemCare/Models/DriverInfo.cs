namespace FendrSystemCare.Models;

/// <summary>
/// Describes an installed device driver as reported by the OS. "Latest" fields
/// are populated when an update search is performed.
/// </summary>
public sealed class DriverInfo
{
    public required string DeviceName { get; init; }
    public string DeviceClass { get; init; } = "Unknown";
    public string Manufacturer { get; init; } = "Unknown";
    public string Provider { get; init; } = "Unknown";
    public string CurrentVersion { get; init; } = "Unknown";
    public DateTime? DriverDate { get; init; }
    public string HardwareId { get; init; } = string.Empty;

    /// <summary>Whether the driver package is digitally signed.</summary>
    public bool IsSigned { get; init; }

    /// <summary>Human-readable signature state for the UI.</summary>
    public string SignatureStatus => IsSigned ? "Signed" : "Unsigned";

    /// <summary>The OEM .inf name (e.g. "oem12.inf") used for backup/restore.</summary>
    public string InfName { get; init; } = string.Empty;

    /// <summary>Populated by an update search; null when no newer driver found.</summary>
    public string? LatestVersion { get; set; }

    /// <summary>True when a device is present but has no working driver.</summary>
    public bool IsMissing { get; init; }

    /// <summary>Convenience flag set when <see cref="LatestVersion"/> is newer.</summary>
    public bool IsOutdated { get; set; }
}
