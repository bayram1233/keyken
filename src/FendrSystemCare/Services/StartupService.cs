using System.Diagnostics;
using System.IO;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>
/// Reads and edits logon startup entries from the registry Run keys and the
/// Startup folders. Enable/disable is implemented the same way Task Manager does
/// it (via the StartupApproved binary flag), so changes are mutually compatible.
/// Delay is implemented by re-hosting the entry as a delayed scheduled task.
/// </summary>
public sealed class StartupService : IStartupService
{
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedRunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly ILoggingService _log;

    public StartupService(ILoggingService log) => _log = log;

    public Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<StartupItem>>(() =>
        {
            var items = new List<StartupItem>();
            ReadRunKey(Registry.CurrentUser, StartupLocation.CurrentUserRun, items);
            ReadRunKey(Registry.LocalMachine, StartupLocation.LocalMachineRun, items);
            ReadStartupFolders(items);
            return items;
        }, ct);

    public Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            if (item.Location == StartupLocation.StartupFolder)
            {
                SetStartupFolderEnabled(item, enabled);
            }
            else
            {
                var root = item.Location == StartupLocation.CurrentUserRun ? Registry.CurrentUser : Registry.LocalMachine;
                using var key = root.CreateSubKey(ApprovedRunPath, writable: true);
                // 12-byte flag: byte 0 = 0x02 enabled, 0x03 disabled; rest is a timestamp.
                var flag = new byte[12];
                flag[0] = (byte)(enabled ? 0x02 : 0x03);
                BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(flag, 4);
                key?.SetValue(item.Name, flag, RegistryValueKind.Binary);
            }

            item.IsEnabled = enabled;
            _log.Info($"Startup '{item.Name}' {(enabled ? "enabled" : "disabled")}.");
        }, ct);

    public Task RemoveAsync(StartupItem item, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            try
            {
                if (item.Location == StartupLocation.StartupFolder)
                {
                    if (File.Exists(item.Command)) File.Delete(item.Command);
                }
                else
                {
                    var root = item.Location == StartupLocation.CurrentUserRun ? Registry.CurrentUser : Registry.LocalMachine;
                    using var run = root.OpenSubKey(RunPath, writable: true);
                    run?.DeleteValue(item.Name, throwOnMissingValue: false);
                    using var approved = root.OpenSubKey(ApprovedRunPath, writable: true);
                    approved?.DeleteValue(item.Name, throwOnMissingValue: false);
                }
                _log.Success($"Startup '{item.Name}' removed.");
            }
            catch (Exception ex)
            {
                _log.Error($"Failed to remove startup '{item.Name}'.", ex);
            }
        }, ct);

    public async Task SetDelayAsync(StartupItem item, int seconds, CancellationToken ct = default)
    {
        // Re-host the entry as a scheduled task that fires at logon after a
        // delay, then disable the original Run entry so it is not launched twice.
        var minutes = Math.Clamp(seconds / 60, 0, 59);
        var taskName = $"FendrSystemCare\\Delay_{Sanitize(item.Name)}";
        var args = $"/create /tn \"{taskName}\" /tr \"{item.Command}\" /sc onlogon /delay 0000:{minutes:00} /rl highest /f";

        var result = await ProcessRunner.RunAsync("schtasks", args, null, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            await SetEnabledAsync(item, false, ct).ConfigureAwait(false);
            item.DelaySeconds = seconds;
            _log.Success($"Startup '{item.Name}' delayed by {minutes} min via scheduled task.");
        }
        else
        {
            _log.Warning($"Failed to set delay for '{item.Name}'.", result.Output);
        }
    }

    // ----- readers ----------------------------------------------------------

    private void ReadRunKey(RegistryKey root, StartupLocation location, List<StartupItem> items)
    {
        try
        {
            using var run = root.OpenSubKey(RunPath);
            if (run is null) return;

            foreach (var name in run.GetValueNames())
            {
                var command = run.GetValue(name)?.ToString() ?? string.Empty;
                var exe = ExtractExecutable(command);
                items.Add(new StartupItem
                {
                    Name = name,
                    Command = command,
                    Location = location,
                    Publisher = GetPublisher(exe),
                    Impact = EstimateImpact(exe),
                    IsEnabled = IsApprovedEnabled(root, name)
                });
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to read Run key for {location}.", ex.Message);
        }
    }

    private void ReadStartupFolders(List<StartupItem> items)
    {
        var folders = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Startup),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup)
        };

        foreach (var folder in folders)
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                if (file.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) continue;
                items.Add(new StartupItem
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Command = file,
                    Location = StartupLocation.StartupFolder,
                    Publisher = GetPublisher(file),
                    Impact = EstimateImpact(file),
                    IsEnabled = true
                });
            }
        }
    }

    private static bool IsApprovedEnabled(RegistryKey root, string name)
    {
        try
        {
            using var approved = root.OpenSubKey(ApprovedRunPath);
            if (approved?.GetValue(name) is byte[] data && data.Length > 0)
                return (data[0] & 0x01) == 0; // even first byte => enabled
        }
        catch { /* Assume enabled when the approval key is absent. */ }
        return true;
    }

    private void SetStartupFolderEnabled(StartupItem item, bool enabled)
    {
        // A folder shortcut is "disabled" by renaming it with a .disabled suffix.
        try
        {
            if (enabled && item.Command.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                var target = item.Command[..^".disabled".Length];
                File.Move(item.Command, target, overwrite: true);
            }
            else if (!enabled && File.Exists(item.Command))
            {
                File.Move(item.Command, item.Command + ".disabled", overwrite: true);
            }
        }
        catch (Exception ex)
        {
            _log.Warning($"Failed to toggle startup folder item '{item.Name}'.", ex.Message);
        }
    }

    // ----- helpers ----------------------------------------------------------

    private static string ExtractExecutable(string command)
    {
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command.Trim('"');
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static string GetPublisher(string exePath)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(exePath);
            if (File.Exists(expanded))
            {
                var company = FileVersionInfo.GetVersionInfo(expanded).CompanyName;
                if (!string.IsNullOrWhiteSpace(company)) return company;
            }
        }
        catch { /* Best effort. */ }
        return "Unknown";
    }

    private static StartupImpact EstimateImpact(string exePath)
    {
        try
        {
            var expanded = Environment.ExpandEnvironmentVariables(exePath);
            if (File.Exists(expanded))
            {
                var mb = new FileInfo(expanded).Length / 1_048_576.0;
                return mb switch { > 50 => StartupImpact.High, > 5 => StartupImpact.Medium, _ => StartupImpact.Low };
            }
        }
        catch { /* Ignore. */ }
        return StartupImpact.Unknown;
    }

    private static string Sanitize(string name) =>
        string.Concat(name.Where(c => char.IsLetterOrDigit(c) || c is '_' or '-'));
}
