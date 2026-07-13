using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>
/// Talks to the Windows Update agent through its COM automation interface
/// (driven from PowerShell) to check, install and list history, and toggles the
/// documented pause registry keys to pause/resume updates.
/// </summary>
public sealed class WindowsUpdateService : IWindowsUpdateService
{
    private const string SettingsKey = @"SOFTWARE\Microsoft\WindowsUpdate\UX\Settings";
    private readonly ILoggingService _log;

    public WindowsUpdateService(ILoggingService log) => _log = log;

    public async Task<IReadOnlyList<WindowsUpdateItem>> CheckForUpdatesAsync(CancellationToken ct = default)
    {
        const string script =
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$se=$s.CreateUpdateSearcher();" +
            "$r=$se.Search('IsInstalled=0 and IsHidden=0');" +
            "$r.Updates | ForEach-Object { $kb=($_.KBArticleIDs -join ','); $sz=[math]::Round($_.MaxDownloadSize/1MB,1); ($_.Title)+'|'+$kb+'|'+$sz+'|'+([int]$_.IsMandatory) }";

        var output = await ProcessRunner.CaptureAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct).ConfigureAwait(false);

        var items = new List<WindowsUpdateItem>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 4) continue;
            items.Add(new WindowsUpdateItem
            {
                Title = parts[0],
                KbArticle = string.IsNullOrWhiteSpace(parts[1]) ? string.Empty : "KB" + parts[1],
                SizeMb = double.TryParse(parts[2], out var mb) ? mb : 0,
                IsMandatory = parts[3].Trim() == "1",
                IsInstalled = false
            });
        }

        _log.Info($"Windows Update check found {items.Count} update(s).");
        return items;
    }

    public async Task InstallUpdatesAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("Downloading and installing updates...");
        const string script =
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$se=$s.CreateUpdateSearcher();" +
            "$r=$se.Search('IsInstalled=0 and IsHidden=0');" +
            "if($r.Updates.Count -eq 0){ 'No updates.'; exit 0 };" +
            "$c=New-Object -ComObject Microsoft.Update.UpdateColl;" +
            "$r.Updates | ForEach-Object { [void]$c.Add($_) };" +
            "$d=$s.CreateUpdateDownloader(); $d.Updates=$c; [void]$d.Download();" +
            "$i=$s.CreateUpdateInstaller(); $i.Updates=$c; $res=$i.Install();" +
            "'Install result code: '+$res.ResultCode+' RebootRequired: '+$res.RebootRequired";

        var result = await ProcessRunner.RunAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", progress, ct).ConfigureAwait(false);

        if (result.ExitCode == 0) _log.Success("Windows Update install finished.");
        else _log.Warning("Windows Update install reported issues.", result.Output);
    }

    public async Task<IReadOnlyList<WindowsUpdateItem>> GetHistoryAsync(CancellationToken ct = default)
    {
        const string script =
            "$s=New-Object -ComObject Microsoft.Update.Session;" +
            "$se=$s.CreateUpdateSearcher();" +
            "$count=$se.GetTotalHistoryCount();" +
            "if($count -gt 0){ $se.QueryHistory(0,[math]::Min($count,100)) | ForEach-Object { ($_.Title)+'|'+($_.Date.ToString('yyyy-MM-dd')) } }";

        var output = await ProcessRunner.CaptureAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", ct).ConfigureAwait(false);

        var items = new List<WindowsUpdateItem>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Trim().Split('|');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0])) continue;
            items.Add(new WindowsUpdateItem
            {
                Title = parts[0],
                IsInstalled = true,
                Date = DateTime.TryParse(parts[1], out var d) ? d : null
            });
        }
        return items;
    }

    public Task PauseUpdatesAsync(int days, CancellationToken ct = default) => Task.Run(() =>
    {
        var now = DateTime.UtcNow;
        var until = now.AddDays(Math.Clamp(days, 1, 35));
        var start = now.ToString("yyyy-MM-ddTHH:mm:ssZ");
        var end = until.ToString("yyyy-MM-ddTHH:mm:ssZ");

        using var key = Registry.LocalMachine.CreateSubKey(SettingsKey, writable: true);
        key?.SetValue("PauseUpdatesStartTime", start);
        key?.SetValue("PauseUpdatesExpiryTime", end);
        key?.SetValue("PauseFeatureUpdatesStartTime", start);
        key?.SetValue("PauseFeatureUpdatesEndTime", end);
        key?.SetValue("PauseQualityUpdatesStartTime", start);
        key?.SetValue("PauseQualityUpdatesEndTime", end);
        _log.Success($"Updates paused until {until:yyyy-MM-dd}.");
    }, ct);

    public Task ResumeUpdatesAsync(CancellationToken ct = default) => Task.Run(() =>
    {
        using var key = Registry.LocalMachine.OpenSubKey(SettingsKey, writable: true);
        if (key is not null)
        {
            foreach (var name in new[]
            {
                "PauseUpdatesStartTime", "PauseUpdatesExpiryTime",
                "PauseFeatureUpdatesStartTime", "PauseFeatureUpdatesEndTime",
                "PauseQualityUpdatesStartTime", "PauseQualityUpdatesEndTime"
            })
            {
                key.DeleteValue(name, throwOnMissingValue: false);
            }
        }
        _log.Success("Updates resumed.");
    }, ct);
}
