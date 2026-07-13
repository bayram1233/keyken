using System.Diagnostics;
using System.IO;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>
/// Applies performance and power tweaks. Registry/service changes are made with
/// well-known, reversible keys; the memory cleaner trims process working sets
/// via the Win32 API. Every action is logged.
/// </summary>
public sealed class PerformanceService : IPerformanceService
{
    // High Performance and Ultimate Performance power scheme GUIDs.
    private const string HighPerformanceGuid = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
    private const string UltimatePerformanceGuid = "e9a42b02-d5df-448d-aa00-03f14749eb61";

    private readonly ILoggingService _log;

    public PerformanceService(ILoggingService log) => _log = log;

    public async Task OptimizeServicesAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        // Set telemetry/low-value services to manual start so they do not run at boot.
        var services = new[] { "DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service" };
        foreach (var svc in services)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report($"Configuring {svc}...");
            await ProcessRunner.RunAsync("sc", $"config {svc} start= demand", null, ct).ConfigureAwait(false);
            await ProcessRunner.RunAsync("sc", $"stop {svc}", null, ct).ConfigureAwait(false);
        }
        _log.Success("Non-essential services optimized.");
    }

    public Task DisableTelemetryAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        SetValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", 0);
        SetValue(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "AllowTelemetry", 0);
        _log.Success("Telemetry disabled.");
    }, ct);

    public Task DisableBackgroundAppsAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        SetValue(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications", "GlobalUserDisabled", 1);
        SetValue(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Search", "BackgroundAppGlobalToggle", 0);
        _log.Success("Background apps disabled.");
    }, ct);

    public async Task OptimizeDrivesAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        // 'defrag /O' picks the right operation automatically: TRIM for SSDs and
        // defragmentation for HDDs.
        foreach (var drive in DriveInfo.GetDrives())
        {
            if (!drive.IsReady || drive.DriveType != DriveType.Fixed) continue;
            ct.ThrowIfCancellationRequested();
            var letter = drive.Name.TrimEnd('\\');
            progress?.Report($"Optimizing {letter}...");
            await ProcessRunner.RunAsync("defrag", $"{letter} /O", progress, ct).ConfigureAwait(false);
        }
        _log.Success("Drive optimization complete.");
    }

    public Task EnableGamingModeAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        SetValue(Registry.CurrentUser, @"SOFTWARE\Microsoft\GameBar", "AutoGameModeEnabled", 1);
        SetValue(Registry.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", 0);
        SetValue(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\GameDVR", "AllowGameDVR", 0);
        _log.Success("Gaming mode enabled.");
    }, ct);

    public Task<long> CleanMemoryAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        long before = 0, after = 0;
        foreach (var process in Process.GetProcesses())
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                before += process.WorkingSet64;
                NativeMethods.EmptyWorkingSet(process.Handle);
                process.Refresh();
                after += process.WorkingSet64;
            }
            catch { /* Protected/system processes are skipped. */ }
            finally { process.Dispose(); }
        }

        var freed = Math.Max(before - after, 0);
        _log.Success("Memory cleaned.", FormatHelper.Bytes(freed));
        return freed;
    }, ct);

    public async Task SetHighPerformancePowerPlanAsync(CancellationToken ct = default)
    {
        // Try Ultimate Performance first (duplicating it if hidden), then fall
        // back to the always-present High Performance plan.
        await ProcessRunner.RunAsync("powercfg", $"-duplicatescheme {UltimatePerformanceGuid}", null, ct).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync("powercfg", $"-setactive {UltimatePerformanceGuid}", null, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
            await ProcessRunner.RunAsync("powercfg", $"-setactive {HighPerformanceGuid}", null, ct).ConfigureAwait(false);
        _log.Success("High performance power plan activated.");
    }

    private void SetValue(RegistryKey root, string path, string name, int value)
    {
        try
        {
            using var key = root.CreateSubKey(path, writable: true);
            key?.SetValue(name, value, RegistryValueKind.DWord);
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to set {path}\\{name}.", ex.Message);
        }
    }
}
