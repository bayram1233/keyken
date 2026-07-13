namespace FendrSystemCare.Models;

/// <summary>
/// One point-in-time sample of live system metrics emitted by
/// <c>IMonitoringService</c> and plotted on the performance/dashboard graphs.
/// </summary>
public sealed class PerformanceSample
{
    public DateTime Timestamp { get; init; } = DateTime.Now;
    public double CpuPercent { get; init; }
    public double RamPercent { get; init; }
    public double GpuPercent { get; init; }
    public double DiskPercent { get; init; }

    /// <summary>Used physical memory in gigabytes.</summary>
    public double RamUsedGb { get; init; }

    /// <summary>Total network throughput across active adapters in megabits/sec.</summary>
    public double NetworkMbps { get; init; }

    /// <summary>Highest thermal-zone temperature in °C, or null when unavailable.</summary>
    public double? TemperatureCelsius { get; init; }

    /// <summary>Reported fan speed in RPM, or null when the hardware does not expose it.</summary>
    public int? FanRpm { get; init; }
}
