using System.Text;
using System.Text.RegularExpressions;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Creates, lists and launches restore of System Restore points via PowerShell
/// cmdlets. Restore points are created before any repair operation so the user
/// can roll back if something goes wrong.
/// </summary>
public sealed class RestorePointService : IRestorePointService
{
    private readonly ILoggingService _log;

    public RestorePointService(ILoggingService log) => _log = log;

    public async Task<bool> CreateAsync(string description, CancellationToken ct = default)
    {
        try
        {
            // Ensure protection is on for the system drive and remove the default
            // 24-hour throttle so consecutive maintenance runs can each snapshot.
            // Kept on a single line so it survives being passed as process args.
            var description2 = Sanitize(description);
            var script =
                "try { Enable-ComputerRestore -Drive $env:SystemDrive } catch {}; " +
                "New-ItemProperty -Path 'HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\SystemRestore' -Name 'SystemRestorePointCreationFrequency' -Value 0 -PropertyType DWord -Force | Out-Null; " +
                $"Checkpoint-Computer -Description '{description2}' -RestorePointType 'MODIFY_SETTINGS'";

            var startedAt = DateTime.Now.AddMinutes(-1);
            var result = await ProcessRunner.RunAsync("powershell",
                $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                null, ct).ConfigureAwait(false);

            // Verify the point actually exists rather than trusting the exit code
            // alone (Checkpoint-Computer can silently no-op under throttling).
            var points = await ListAsync(ct).ConfigureAwait(false);
            var verified = points.Any(p => p.Created >= startedAt);

            if (verified)
            {
                _log.Success("Restore point created and verified.", description);
                return true;
            }

            _log.Warning("Restore point could not be verified after creation.", result.Output);
            return false;
        }
        catch (Exception ex)
        {
            _log.Error("Failed to create restore point.", ex);
            return false;
        }
    }

    public async Task<IReadOnlyList<(int Sequence, string Description, DateTime Created)>> ListAsync(CancellationToken ct = default)
    {
        var list = new List<(int, string, DateTime)>();
        try
        {
            var output = await ProcessRunner.CaptureAsync("powershell",
                "-NoProfile -ExecutionPolicy Bypass -Command \"Get-ComputerRestorePoint | Select-Object SequenceNumber,Description,CreationTime | Format-Table -HideTableHeaders\"",
                ct).ConfigureAwait(false);

            foreach (var raw in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var line = raw.Trim();
                var match = Regex.Match(line, @"^(\d+)\s+(.*?)\s+(\d{14})");
                if (match.Success &&
                    int.TryParse(match.Groups[1].Value, out var seq) &&
                    DateTime.TryParseExact(match.Groups[3].Value, "yyyyMMddHHmmss", null,
                        System.Globalization.DateTimeStyles.None, out var created))
                {
                    list.Add((seq, match.Groups[2].Value.Trim(), created));
                }
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to list restore points.", ex);
        }

        return list;
    }

    public async Task RestoreAsync(int sequence, CancellationToken ct = default)
    {
        // Launch the built-in restore UI so the user explicitly confirms the
        // (reboot-inducing) rollback rather than performing it silently.
        _log.Info($"Launching System Restore wizard (point {sequence}).");
        await ProcessRunner.RunAsync("rstrui.exe", string.Empty, null, ct).ConfigureAwait(false);
    }

    private static string Sanitize(string input) =>
        Regex.Replace(input, "['\"`$]", string.Empty);
}
