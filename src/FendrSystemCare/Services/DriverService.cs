using System.IO;
using System.Management;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Enumerates and services device drivers. Inventory comes from WMI
/// (Win32_PnPSignedDriver); backup/restore/export use pnputil; update searches
/// use the Windows Update agent so only Microsoft-signed driver packages are
/// applied. A restore point should be created by the caller beforehand.
/// </summary>
public sealed class DriverService : IDriverService
{
    private readonly ILoggingService _log;
    private readonly IRestorePointService _restore;

    public DriverService(ILoggingService log, IRestorePointService restore)
    {
        _log = log;
        _restore = restore;
    }

    public Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DriverInfo>>(() =>
        {
            var drivers = new List<DriverInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, HardWareID, InfName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
                foreach (var o in searcher.Get())
                {
                    ct.ThrowIfCancellationRequested();
                    drivers.Add(new DriverInfo
                    {
                        DeviceName = Str(o["DeviceName"]),
                        DeviceClass = Str(o["DeviceClass"]),
                        Manufacturer = Str(o["Manufacturer"]),
                        Provider = Str(o["DriverProviderName"]),
                        CurrentVersion = Str(o["DriverVersion"]),
                        DriverDate = ParseWmiDate(o["DriverDate"]),
                        HardwareId = FirstHardwareId(o["HardWareID"]),
                        InfName = Str(o["InfName"]),
                        IsSigned = o["IsSigned"] is bool signed && signed
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Error("Failed to enumerate drivers.", ex);
            }

            return drivers
                .GroupBy(d => d.DeviceName)
                .Select(g => g.First())
                .OrderBy(d => d.DeviceClass)
                .ToList();
        }, ct);

    public Task<IReadOnlyList<DriverInfo>> DetectProblemDevicesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DriverInfo>>(() =>
        {
            var problems = new List<DriverInfo>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PNPClass, Manufacturer, ConfigManagerErrorCode, HardWareID FROM Win32_PnPEntity WHERE ConfigManagerErrorCode <> 0");
                foreach (var o in searcher.Get())
                {
                    ct.ThrowIfCancellationRequested();
                    problems.Add(new DriverInfo
                    {
                        DeviceName = Str(o["Name"]),
                        DeviceClass = Str(o["PNPClass"]),
                        Manufacturer = Str(o["Manufacturer"]),
                        HardwareId = FirstHardwareId(o["HardWareID"]),
                        IsMissing = true
                    });
                }
            }
            catch (Exception ex)
            {
                _log.Error("Failed to detect problem devices.", ex);
            }
            return problems;
        }, ct);

    public async Task ScanForUpdatesAsync(IEnumerable<DriverInfo> drivers, IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("Querying Windows Update for driver packages...");

        // Ask the Windows Update agent which driver updates are applicable.
        const string script =
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$se=$s.CreateUpdateSearcher();" +
            "$r=$se.Search(\\\"IsInstalled=0 and Type='Driver'\\\");" +
            "$r.Updates | ForEach-Object { $_.Title }";

        var output = await ProcessRunner.CaptureAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct).ConfigureAwait(false);

        var titles = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        var driverList = drivers.ToList();
        foreach (var driver in driverList)
        {
            ct.ThrowIfCancellationRequested();
            var match = titles.FirstOrDefault(t =>
                t.Contains(driver.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(driver.Provider) && t.Contains(driver.Provider, StringComparison.OrdinalIgnoreCase)));

            if (match is not null)
            {
                driver.IsOutdated = true;
                driver.LatestVersion = "Update available via Windows Update";
            }
        }

        var count = driverList.Count(d => d.IsOutdated);
        progress?.Report($"Scan complete. {count} driver update(s) available.");
        _log.Info($"Driver update scan finished. {count} update(s) found.");
    }

    public async Task<bool> BackupDriversAsync(string destinationFolder, IProgress<string>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);
        progress?.Report("Exporting all third-party drivers...");
        var result = await ProcessRunner.RunAsync("pnputil",
            $"/export-driver * \"{destinationFolder}\"", progress, ct).ConfigureAwait(false);

        var ok = result.ExitCode == 0;
        if (ok) _log.Success("Drivers backed up.", destinationFolder);
        else _log.Warning("Driver backup returned a non-zero exit code.", result.Output);
        return ok;
    }

    public async Task<bool> RestoreDriverAsync(string infPath, CancellationToken ct = default)
    {
        await _restore.CreateAsync("Fendr System Care - before driver restore", ct).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync("pnputil",
            $"/add-driver \"{infPath}\" /install", null, ct).ConfigureAwait(false);
        var ok = result.ExitCode is 0 or 3010; // 3010 = success, reboot required
        if (ok) _log.Success("Driver restored.", infPath);
        else _log.Warning("Driver restore failed.", result.Output);
        return ok;
    }

    public async Task<bool> ExportDriverAsync(DriverInfo driver, string destinationFolder, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(driver.InfName))
        {
            _log.Warning("Cannot export driver without an INF name.", driver.DeviceName);
            return false;
        }

        Directory.CreateDirectory(destinationFolder);
        var result = await ProcessRunner.RunAsync("pnputil",
            $"/export-driver {driver.InfName} \"{destinationFolder}\"", null, ct).ConfigureAwait(false);
        var ok = result.ExitCode == 0;
        if (ok) _log.Success($"Exported driver '{driver.DeviceName}'.", destinationFolder);
        return ok;
    }

    public async Task<bool> UpdateDriverAsync(DriverInfo driver, IProgress<string>? progress, CancellationToken ct = default)
    {
        await _restore.CreateAsync($"Fendr System Care - before updating {driver.DeviceName}", ct).ConfigureAwait(false);
        progress?.Report($"Installing update for {driver.DeviceName} via Windows Update...");

        // Download and install the matching driver update through the WU agent.
        var script =
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$se=$s.CreateUpdateSearcher();" +
            "$r=$se.Search(\\\"IsInstalled=0 and Type='Driver'\\\");" +
            "$c=New-Object -ComObject Microsoft.Update.UpdateColl;" +
            $"$r.Updates | Where-Object {{ $_.Title -like '*{Escape(driver.DeviceName)}*' }} | ForEach-Object {{ [void]$c.Add($_) }};" +
            "if($c.Count -eq 0){ 'NO_MATCH'; exit 0 };" +
            "$d=$s.CreateUpdateDownloader(); $d.Updates=$c; [void]$d.Download();" +
            "$i=$s.CreateUpdateInstaller(); $i.Updates=$c; $res=$i.Install(); 'RESULT:'+$res.ResultCode";

        var result = await ProcessRunner.RunAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", progress, ct).ConfigureAwait(false);

        var ok = result.Output.Contains("RESULT:2"); // 2 = orcSucceeded
        if (ok) _log.Success($"Driver updated: {driver.DeviceName}.");
        else _log.Warning($"No update installed for {driver.DeviceName}.", result.Output.Trim());
        return ok;
    }

    // ----- helpers ----------------------------------------------------------

    private static string Str(object? value) => value?.ToString()?.Trim() ?? "Unknown";

    private static string FirstHardwareId(object? value) => value switch
    {
        string[] arr when arr.Length > 0 => arr[0],
        string s => s,
        _ => string.Empty
    };

    private static DateTime? ParseWmiDate(object? value)
    {
        var raw = value?.ToString();
        if (string.IsNullOrEmpty(raw) || raw.Length < 8) return null;
        return DateTime.TryParseExact(raw[..8], "yyyyMMdd", null,
            System.Globalization.DateTimeStyles.None, out var date) ? date : null;
    }

    private static string Escape(string input) => input.Replace("'", "''");
}
