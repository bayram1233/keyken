using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>
/// Collects static hardware/OS facts through WMI and the registry, enumerates
/// storage volumes, measures a rough internet speed and derives an overall
/// health score. All queries run on a background thread to keep the UI fluid.
/// </summary>
public sealed class SystemInfoService : ISystemInfoService
{
    private readonly ILoggingService _log;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public SystemInfoService(ILoggingService log) => _log = log;

    public Task<SystemInformation> GetSystemInformationAsync(CancellationToken ct = default) =>
        Task.Run(() =>
        {
            ct.ThrowIfCancellationRequested();

            var (cpuName, cores, logical, clock) = QueryCpu();
            var (gpuName, gpuMem, gpuDriver) = QueryGpu();
            var (ramGb, ramType, ramSpeed) = QueryMemory();
            var (edition, version, build, isWin11) = QueryWindows();
            var (biosVer, biosDate) = QueryBios();
            var (boardMan, boardProd) = QueryMotherboard();
            var (activated, activation) = QueryActivation();

            return new SystemInformation
            {
                CpuName = cpuName,
                CpuCores = cores,
                CpuLogicalProcessors = logical,
                CpuMaxClockGhz = Math.Round(clock / 1000.0, 2),
                GpuName = gpuName,
                GpuMemoryGb = gpuMem,
                GpuDriverVersion = gpuDriver,
                TotalRamGb = ramGb,
                RamType = ramType,
                RamSpeedMhz = ramSpeed,
                WindowsEdition = edition,
                WindowsVersion = version,
                WindowsBuild = build,
                IsWindows11 = isWin11,
                BiosVersion = biosVer,
                BiosDate = biosDate,
                MotherboardManufacturer = boardMan,
                MotherboardProduct = boardProd,
                IsActivated = activated,
                ActivationStatus = activation,
                Disks = QueryDiskHealth()
            };
        }, ct);

    public Task<IReadOnlyList<StorageVolume>> GetStorageVolumesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<StorageVolume>>(() =>
        {
            var volumes = new List<StorageVolume>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                ct.ThrowIfCancellationRequested();
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;

                volumes.Add(new StorageVolume
                {
                    Drive = drive.Name.TrimEnd('\\'),
                    Label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? "Local Disk" : drive.VolumeLabel,
                    TotalGb = Math.Round(drive.TotalSize / 1_073_741_824.0, 1),
                    FreeGb = Math.Round(drive.TotalFreeSpace / 1_073_741_824.0, 1)
                });
            }
            return volumes;
        }, ct);

    public async Task<HealthScore> ComputeHealthScoreAsync(CancellationToken ct = default)
    {
        var findings = new List<string>();
        var score = 100;

        var info = await GetSystemInformationAsync(ct).ConfigureAwait(false);
        var volumes = await GetStorageVolumesAsync(ct).ConfigureAwait(false);

        foreach (var v in volumes)
        {
            if (v.TotalGb > 0 && v.FreeGb / v.TotalGb < 0.10)
            {
                score -= 15;
                findings.Add($"Low free space on {v.Drive} ({v.FreeGb:0} GB free).");
            }
        }

        foreach (var d in info.Disks)
        {
            if (!string.Equals(d.HealthStatus, "Healthy", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(d.HealthStatus, "OK", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(d.HealthStatus, "Unknown", StringComparison.OrdinalIgnoreCase))
            {
                score -= 25;
                findings.Add($"Disk '{d.Model}' reports health: {d.HealthStatus}.");
            }
        }

        if (!info.IsActivated)
        {
            score -= 10;
            findings.Add("Windows is not activated.");
        }

        var problemDevices = QueryProblemDeviceCount();
        if (problemDevices > 0)
        {
            score -= Math.Min(problemDevices * 5, 20);
            findings.Add($"{problemDevices} device(s) have a driver problem.");
        }

        if (IsRebootPending())
        {
            score -= 5;
            findings.Add("A reboot is pending to finish updates/installs.");
        }

        var startupCount = QueryStartupCount();
        if (startupCount > 8)
        {
            score -= 5;
            findings.Add($"{startupCount} startup programs may slow sign-in.");
        }

        var temperature = QueryMaxTemperature();
        if (temperature is > 85)
        {
            score -= 10;
            findings.Add($"High system temperature detected ({temperature:0} °C).");
        }

        score = Math.Clamp(score, 0, 100);
        var rating = score switch
        {
            >= 90 => "Excellent",
            >= 75 => "Good",
            >= 50 => "Fair",
            _ => "Needs attention"
        };

        if (findings.Count == 0)
            findings.Add("No issues detected.");

        return new HealthScore { Score = score, Rating = rating, Findings = findings };
    }

    public Task<TimeSpan> GetUptimeAsync(CancellationToken ct = default) =>
        Task.FromResult(TimeSpan.FromMilliseconds(Environment.TickCount64));

    public async Task<double> MeasureInternetSpeedMbpsAsync(CancellationToken ct = default)
    {
        // Download a small fixed-size payload from a fast CDN and compute the
        // effective throughput. Returns 0 when offline.
        const string url = "https://speed.cloudflare.com/__down?bytes=10000000"; // 10 MB
        try
        {
            var sw = Stopwatch.StartNew();
            using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            sw.Stop();

            var seconds = Math.Max(sw.Elapsed.TotalSeconds, 0.001);
            var mbps = bytes.Length * 8 / 1_000_000.0 / seconds;
            return Math.Round(mbps, 1);
        }
        catch (Exception ex)
        {
            _log.Warning("Internet speed test failed.", ex.Message);
            return 0;
        }
    }

    // ----- WMI / registry query helpers -------------------------------------

    private (string, int, int, double) QueryCpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed FROM Win32_Processor");
            foreach (var o in searcher.Get())
            {
                return (
                    Str(o["Name"]),
                    Int(o["NumberOfCores"]),
                    Int(o["NumberOfLogicalProcessors"]),
                    Int(o["MaxClockSpeed"]));
            }
        }
        catch (Exception ex) { _log.Warning("CPU query failed.", ex.Message); }
        return ("Unknown", 0, 0, 0);
    }

    private (string, double, string) QueryGpu()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, AdapterRAM, DriverVersion FROM Win32_VideoController");
            foreach (var o in searcher.Get())
            {
                var ramBytes = o["AdapterRAM"] is null ? 0L : Convert.ToInt64(o["AdapterRAM"]);
                return (Str(o["Name"]), Math.Round(ramBytes / 1_073_741_824.0, 1), Str(o["DriverVersion"]));
            }
        }
        catch (Exception ex) { _log.Warning("GPU query failed.", ex.Message); }
        return ("Unknown", 0, "Unknown");
    }

    private (double, string, double) QueryMemory()
    {
        double totalGb = 0, speed = 0;
        var type = "Unknown";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Capacity, Speed, SMBIOSMemoryType FROM Win32_PhysicalMemory");
            foreach (var o in searcher.Get())
            {
                totalGb += o["Capacity"] is null ? 0 : Convert.ToInt64(o["Capacity"]) / 1_073_741_824.0;
                speed = Int(o["Speed"]);
                type = MapMemoryType(Int(o["SMBIOSMemoryType"]));
            }
        }
        catch (Exception ex) { _log.Warning("Memory query failed.", ex.Message); }
        return (Math.Round(totalGb, 1), type, speed);
    }

    private (string, string, string, bool) QueryWindows()
    {
        var edition = "Windows";
        var build = "0";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Caption, BuildNumber FROM Win32_OperatingSystem");
            foreach (var o in searcher.Get())
            {
                edition = Str(o["Caption"]);
                build = Str(o["BuildNumber"]);
            }
        }
        catch (Exception ex) { _log.Warning("OS query failed.", ex.Message); }

        var displayVersion = "";
        var ubr = "";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            displayVersion = key?.GetValue("DisplayVersion")?.ToString() ?? "";
            ubr = key?.GetValue("UBR")?.ToString() ?? "";
        }
        catch { /* Best effort. */ }

        var buildNumber = int.TryParse(build, out var b) ? b : 0;
        var isWin11 = buildNumber >= 22000;
        if (isWin11) edition = edition.Replace("Windows 10", "Windows 11");

        var fullBuild = string.IsNullOrEmpty(ubr) ? build : $"{build}.{ubr}";
        return (edition, displayVersion, fullBuild, isWin11);
    }

    private (string, string) QueryBios()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SMBIOSBIOSVersion, ReleaseDate FROM Win32_BIOS");
            foreach (var o in searcher.Get())
            {
                var date = Str(o["ReleaseDate"]);
                if (date.Length >= 8)
                    date = $"{date.Substring(0, 4)}-{date.Substring(4, 2)}-{date.Substring(6, 2)}";
                return (Str(o["SMBIOSBIOSVersion"]), date);
            }
        }
        catch (Exception ex) { _log.Warning("BIOS query failed.", ex.Message); }
        return ("Unknown", "Unknown");
    }

    private (string, string) QueryMotherboard()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, Product FROM Win32_BaseBoard");
            foreach (var o in searcher.Get())
                return (Str(o["Manufacturer"]), Str(o["Product"]));
        }
        catch (Exception ex) { _log.Warning("Motherboard query failed.", ex.Message); }
        return ("Unknown", "Unknown");
    }

    private (bool, string) QueryActivation()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT LicenseStatus, PartialProductKey FROM SoftwareLicensingProduct WHERE ApplicationID='55c92734-d682-4d71-983e-d6ec3f16059f' AND PartialProductKey IS NOT NULL");
            foreach (var o in searcher.Get())
            {
                var status = Int(o["LicenseStatus"]);
                return status == 1 ? (true, "Activated") : (false, "Not activated");
            }
        }
        catch (Exception ex) { _log.Warning("Activation query failed.", ex.Message); }
        return (false, "Unknown");
    }

    private IReadOnlyList<DiskHealth> QueryDiskHealth()
    {
        var disks = new List<DiskHealth>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\Microsoft\Windows\Storage");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT Model, MediaType, Size, HealthStatus FROM MSFT_PhysicalDisk"));
            foreach (var o in searcher.Get())
            {
                disks.Add(new DiskHealth
                {
                    Model = Str(o["Model"]),
                    MediaType = MapDiskMedia(Int(o["MediaType"])),
                    SizeGb = o["Size"] is null ? 0 : Math.Round(Convert.ToInt64(o["Size"]) / 1_073_741_824.0, 0),
                    HealthStatus = MapHealth(Int(o["HealthStatus"]))
                });
            }
        }
        catch (Exception ex) { _log.Warning("Disk health query failed.", ex.Message); }
        return disks;
    }

    // ----- health metric helpers --------------------------------------------

    private int QueryProblemDeviceCount()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT ConfigManagerErrorCode FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
            return searcher.Get().Count;
        }
        catch (Exception ex) { _log.Warning("Problem-device query failed.", ex.Message); return 0; }
    }

    private static bool IsRebootPending()
    {
        try
        {
            using var cbs = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending");
            if (cbs is not null) return true;

            using var wu = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired");
            return wu is not null;
        }
        catch { return false; }
    }

    private static int QueryStartupCount()
    {
        var count = 0;
        try
        {
            foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run");
                count += key?.GetValueNames().Length ?? 0;
            }
        }
        catch { /* Best effort. */ }
        return count;
    }

    private double? QueryMaxTemperature()
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
                var celsius = Convert.ToDouble(o["CurrentTemperature"]) / 10.0 - 273.15;
                if (celsius is > 0 and < 150 && (max is null || celsius > max)) max = celsius;
            }
            return max;
        }
        catch { return null; }
    }

    // ----- value mappers ----------------------------------------------------

    private static string Str(object? value) => value?.ToString()?.Trim() ?? "Unknown";
    private static int Int(object? value)
    {
        try { return value is null ? 0 : Convert.ToInt32(value); }
        catch { return 0; }
    }

    private static string MapMemoryType(int type) => type switch
    {
        20 => "DDR", 21 => "DDR2", 24 => "DDR3", 26 => "DDR4", 34 => "DDR5",
        _ => "RAM"
    };

    private static string MapDiskMedia(int media) => media switch
    {
        3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Unspecified"
    };

    private static string MapHealth(int status) => status switch
    {
        0 => "Healthy", 1 => "Warning", 2 => "Unhealthy", _ => "Unknown"
    };
}
