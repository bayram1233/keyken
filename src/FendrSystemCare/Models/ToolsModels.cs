namespace FendrSystemCare.Models;

/// <summary>A program listed under Programs &amp; Features / Apps.</summary>
public sealed class InstalledProgram
{
    public required string Name { get; init; }
    public string Version { get; init; } = string.Empty;
    public string Publisher { get; init; } = string.Empty;
    public string InstallDate { get; init; } = string.Empty;
    public string? UninstallString { get; init; }
}

/// <summary>A Windows service and its current configuration.</summary>
public sealed class ServiceItem
{
    public required string Name { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string StartType { get; init; } = string.Empty;
}

/// <summary>A single network adapter's addressing information.</summary>
public sealed class NetworkAdapterInfo
{
    public required string Name { get; init; }
    public string Description { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public string Ipv4 { get; init; } = string.Empty;
    public string Ipv6 { get; init; } = string.Empty;
    public string Gateway { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
