using System.Diagnostics;
using System.Management;
using System.Timers;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using Timer = System.Timers.Timer;

namespace FendrSystemCare.Services;

/// <summary>
/// Samples CPU, RAM, GPU and disk utilisation once per second using Windows
/// performance counters and raises <see cref="SampleAvailable"/> for live
/// graphs. Counter access degrades gracefully when a category is unavailable.
/// </summary>
public sealed class MonitoringService : IMonitoringService, IDisposable
{
    private readonly ILoggingService _log;
    private readonly Timer _timer = new(1000) { AutoReset = true };

    private PerformanceCounter? _cpu;
    private PerformanceCounter? _availableMb;
    private PerformanceCounter? _disk;
    private List<PerformanceCounter>? _gpuCounters;
    private List<PerformanceCounter>? _netCounters;

    private double _totalRamMb;
    private bool _initialised;

    // Temperature/fan come from WMI which is comparatively expensive, so they are
    // refreshed only every few ticks and cached between reads.
    private int _tickCount;
    private double? _cachedTemperature;
    private int? _cachedFanRpm;
    private const int SlowSampleEvery = 5;

    public event EventHandler<PerformanceSample>? SampleAvailable;
    public PerformanceSample Latest { get; private set; } = new();

    public MonitoringService(ILoggingService log)
    {
        _log = log;
        _timer.Elapsed += OnTick;
    }

    public void Start()
    {
        if (!_initialised) Initialise();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    private void Initialise()
    {
        try { _cpu = new PerformanceCounter("Processor", "% Processor Time", "_Total"); } catch { }
        try { _availableMb = new PerformanceCounter("Memory", "Available MBytes"); } catch { }
        try { _disk = new PerformanceCounter("PhysicalDisk", "% Disk Time", "_Total"); } catch { }
        try { _gpuCounters = LoadGpuCounters(); } catch { }
        try { _netCounters = LoadNetworkCounters(); } catch { }
        try { _totalRamMb = QueryTotalRamMb(); } catch { }

        // First read primes the counters (they return 0 on the very first call).
        _cpu?.NextValue();
        _disk?.NextValue();
        _initialised = true;
    }

    private void OnTick(object? sender, ElapsedEventArgs e)
    {
        try
        {
            var cpu = Clamp(_cpu?.NextValue() ?? 0);
            var disk = Clamp(_disk?.NextValue() ?? 0);
            var availableMb = _availableMb?.NextValue() ?? 0;
            var usedMb = Math.Max(_totalRamMb - availableMb, 0);
            var ramPercent = _totalRamMb > 0 ? Clamp(usedMb / _totalRamMb * 100) : 0;
            var gpu = ReadGpu();
            var networkMbps = ReadNetworkMbps();

            // Refresh the expensive WMI sensors only occasionally.
            if (_tickCount++ % SlowSampleEvery == 0)
            {
                _cachedTemperature = ReadTemperature();
                _cachedFanRpm = ReadFanRpm();
            }

            var sample = new PerformanceSample
            {
                CpuPercent = Math.Round(cpu, 1),
                RamPercent = Math.Round(ramPercent, 1),
                RamUsedGb = Math.Round(usedMb / 1024.0, 1),
                GpuPercent = Math.Round(gpu, 1),
                DiskPercent = Math.Round(disk, 1),
                NetworkMbps = Math.Round(networkMbps, 2),
                TemperatureCelsius = _cachedTemperature,
                FanRpm = _cachedFanRpm
            };

            Latest = sample;
            SampleAvailable?.Invoke(this, sample);
        }
        catch (Exception ex)
        {
            _log.Warning("Monitoring sample failed.", ex.Message);
        }
    }

    private double ReadGpu()
    {
        if (_gpuCounters is null || _gpuCounters.Count == 0) return 0;
        double total = 0;
        foreach (var c in _gpuCounters)
        {
            try { total += c.NextValue(); } catch { }
        }
        return Clamp(total);
    }

    private static List<PerformanceCounter> LoadGpuCounters()
    {
        var counters = new List<PerformanceCounter>();
        var category = new PerformanceCounterCategory("GPU Engine");
        foreach (var instance in category.GetInstanceNames())
        {
            if (!instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) continue;
            counters.Add(new PerformanceCounter("GPU Engine", "Utilization Percentage", instance));
        }
        return counters;
    }

    private static List<PerformanceCounter> LoadNetworkCounters()
    {
        var counters = new List<PerformanceCounter>();
        var category = new PerformanceCounterCategory("Network Interface");
        foreach (var instance in category.GetInstanceNames())
        {
            if (instance.Contains("Loopback", StringComparison.OrdinalIgnoreCase)) continue;
            counters.Add(new PerformanceCounter("Network Interface", "Bytes Total/sec", instance));
        }
        return counters;
    }

    private double ReadNetworkMbps()
    {
        if (_netCounters is null || _netCounters.Count == 0) return 0;
        double bytesPerSec = 0;
        foreach (var c in _netCounters)
        {
            try { bytesPerSec += c.NextValue(); } catch { }
        }
        return bytesPerSec * 8 / 1_000_000.0;
    }

    private double? ReadTemperature()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\WMI");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"));
            double? max = null;
            foreach (var o in searcher.Get())
            {
                if (o["CurrentTemperature"] is null) continue;
                // Value is reported in tenths of a Kelvin.
                var celsius = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                if (celsius is > 0 and < 150 && (max is null || celsius > max))
                    max = Math.Round(celsius, 1);
            }
            return max;
        }
        catch
        {
            // Many systems do not expose ACPI thermal zones to WMI.
            return null;
        }
    }

    private int? ReadFanRpm()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DesiredSpeed FROM Win32_Fan");
            foreach (var o in searcher.Get())
            {
                if (o["DesiredSpeed"] is null) continue;
                var rpm = Convert.ToInt32(o["DesiredSpeed"]);
                if (rpm > 0) return rpm;
            }
        }
        catch { /* Fan telemetry is rarely exposed on consumer hardware. */ }
        return null;
    }

    private static double QueryTotalRamMb()
    {
        using var searcher = new ManagementObjectSearcher(
            "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem");
        foreach (var o in searcher.Get())
            return Convert.ToInt64(o["TotalPhysicalMemory"]) / 1_048_576.0;
        return 0;
    }

    private static double Clamp(double value) => Math.Clamp(value, 0, 100);

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        _cpu?.Dispose();
        _availableMb?.Dispose();
        _disk?.Dispose();
        if (_gpuCounters is not null)
            foreach (var c in _gpuCounters) c.Dispose();
        if (_netCounters is not null)
            foreach (var c in _netCounters) c.Dispose();
    }
}
