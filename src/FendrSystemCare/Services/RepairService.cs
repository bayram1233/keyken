using System.Diagnostics;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Executes Windows repair tooling. Each <see cref="RepairTask"/> maps to a real
/// command line (SFC, DISM, CHKDSK, netsh, service control, PowerShell scans).
/// Output is streamed live so the UI can show progress and the user can cancel.
/// </summary>
public sealed class RepairService : IRepairService
{
    private readonly ILoggingService _log;
    private readonly List<RepairTask> _tasks;

    public RepairService(ILoggingService log)
    {
        _log = log;
        _tasks = BuildTasks();
    }

    public IReadOnlyList<RepairTask> GetTasks() => _tasks;

    public async Task<RepairResult> RunAsync(RepairTask task, IProgress<string>? output, CancellationToken ct = default)
    {
        var sw = Stopwatch.StartNew();
        task.Status = OperationStatus.Running;
        using var scope = _log.BeginOperation($"Repair: {task.Name}", $"{task.FileName} {task.Arguments}");
        output?.Report($"> {task.FileName} {task.Arguments}");

        try
        {
            var result = await ProcessRunner.RunAsync(task.FileName, task.Arguments, output, ct).ConfigureAwait(false);
            sw.Stop();

            var status = result.ExitCode == 0 ? OperationStatus.Completed : OperationStatus.Failed;
            task.Status = status;

            if (status == OperationStatus.Completed)
                scope.Complete($"Exit code {result.ExitCode}");
            else
                scope.Fail($"Exit code {result.ExitCode}");

            return new RepairResult
            {
                TaskKey = task.Key,
                Status = status,
                ExitCode = result.ExitCode,
                Output = result.Output,
                Duration = sw.Elapsed
            };
        }
        catch (OperationCanceledException)
        {
            task.Status = OperationStatus.Cancelled;
            _log.Warning($"Repair cancelled: {task.Name}");
            return new RepairResult { TaskKey = task.Key, Status = OperationStatus.Cancelled, Duration = sw.Elapsed };
        }
        catch (Exception ex)
        {
            task.Status = OperationStatus.Failed;
            scope.Fail(ex.Message, ex);
            return new RepairResult { TaskKey = task.Key, Status = OperationStatus.Failed, Output = ex.Message, Duration = sw.Elapsed };
        }
    }

    private static List<RepairTask> BuildTasks() => new()
    {
        new("sfc", "SFC /scannow", "Scans and repairs protected system files.", "ShieldCheckmark24",
            "sfc", "/scannow"),
        new("dism_check", "DISM CheckHealth", "Quick check for component store corruption.", "Search24",
            "DISM", "/Online /Cleanup-Image /CheckHealth"),
        new("dism_scan", "DISM ScanHealth", "Thorough scan of the component store.", "SearchInfo24",
            "DISM", "/Online /Cleanup-Image /ScanHealth"),
        new("dism_restore", "DISM RestoreHealth", "Repairs the Windows component store.", "WrenchScrewdriver24",
            "DISM", "/Online /Cleanup-Image /RestoreHealth"),
        new("chkdsk", "CHKDSK", "Read-only scan of the system drive.", "HardDrive24",
            "chkdsk", "C: /scan"),
        new("component_repair", "Windows Component Repair", "Cleans up superseded components.", "Broom24",
            "DISM", "/Online /Cleanup-Image /StartComponentCleanup"),
        new("registry_scan", "Registry Error Scan", "Reports uninstall entries with missing targets.", "Database24",
            "powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Get-ChildItem 'HKLM:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Uninstall' | ForEach-Object { $p=(Get-ItemProperty $_.PSPath).InstallLocation; if($p -and -not (Test-Path $p)){ 'Missing: ' + $_.PSChildName + ' -> ' + $p } }\"",
            requiresConfirmation: false),
        new("broken_shortcuts", "Broken Shortcut Scan", "Finds Start Menu shortcuts whose target is gone.", "Link24",
            "powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"$s=New-Object -ComObject WScript.Shell; Get-ChildItem \\\"$env:ProgramData\\Microsoft\\Windows\\Start Menu\\\" -Recurse -Filter *.lnk | ForEach-Object { $t=$s.CreateShortcut($_.FullName).TargetPath; if($t -and -not (Test-Path $t)){ 'Broken: ' + $_.Name } }\"",
            requiresConfirmation: false),
        new("broken_services", "Broken Services", "Lists services whose executable is missing.", "PlugDisconnected24",
            "powershell", "-NoProfile -ExecutionPolicy Bypass -Command \"Get-CimInstance Win32_Service | ForEach-Object { $exe=($_.PathName -replace '^\\\"([^\\\"]+)\\\".*','$1'); if($exe -and $exe.EndsWith('.exe') -and -not (Test-Path $exe)){ 'Missing exe: ' + $_.Name } }\"",
            requiresConfirmation: false),
        new("services_repair", "Windows Services Repair", "Restores core services to their default start type.", "Settings24",
            "cmd", "/c sc config wuauserv start= auto & sc config bits start= delayed-auto & sc config wscsvc start= auto & sc config WSearch start= delayed-auto & echo Core service start types restored."),
        new("reset_network", "Reset Network", "Resets the TCP/IP stack.", "NetworkCheck24",
            "netsh", "int ip reset"),
        new("flush_dns", "Flush DNS", "Clears the DNS resolver cache.", "GlobeClock24",
            "ipconfig", "/flushdns", requiresConfirmation: false),
        new("reset_winsock", "Reset Winsock", "Resets the Winsock catalog.", "Router24",
            "netsh", "winsock reset"),
        new("repair_store", "Repair Microsoft Store", "Resets the Microsoft Store cache.", "Store24",
            "wsreset.exe", string.Empty),
        new("repair_wu", "Repair Windows Update", "Resets Windows Update components.", "ArrowSync24",
            "cmd", "/c net stop wuauserv & net stop bits & ren \"%windir%\\SoftwareDistribution\" SoftwareDistribution.old & net start bits & net start wuauserv & echo Windows Update components reset."),
        new("repair_firewall", "Repair Firewall", "Resets Windows Firewall to defaults.", "ShieldTask24",
            "netsh", "advfirewall reset"),
        new("repair_defender", "Repair Windows Defender", "Updates Defender security intelligence.", "ShieldCheckmark24",
            "cmd", "/c \"\"%ProgramFiles%\\Windows Defender\\MpCmdRun.exe\" -SignatureUpdate\""),
        new("repair_print", "Repair Printing Services", "Restarts the print spooler and clears the queue.", "Print24",
            "cmd", "/c net stop spooler & del /q /f /s \"%systemroot%\\System32\\spool\\PRINTERS\\*.*\" & net start spooler & echo Print spooler repaired."),
        new("repair_audio", "Repair Audio Services", "Restarts the Windows audio services.", "Speaker224",
            "cmd", "/c net stop audiosrv & net stop AudioEndpointBuilder & net start AudioEndpointBuilder & net start audiosrv & echo Audio services restarted."),
        new("repair_bluetooth", "Repair Bluetooth", "Restarts the Bluetooth support service.", "Bluetooth24",
            "cmd", "/c net stop bthserv & net start bthserv & echo Bluetooth service restarted."),
        new("repair_wmi", "Repair WMI Repository", "Verifies and salvages the WMI repository.", "Database24",
            "cmd", "/c winmgmt /verifyrepository & winmgmt /salvagerepository & echo WMI repository checked."),
        new("repair_powershell", "Repair PowerShell", "Restores a safe PowerShell execution policy.", "Code24",
            "powershell", "-NoProfile -Command \"Set-ExecutionPolicy -Scope LocalMachine RemoteSigned -Force; 'Execution policy set to RemoteSigned.'\""),
        new("restart_explorer", "Restart Explorer", "Restarts the Windows Explorer shell.", "WindowApps24",
            "cmd", "/c taskkill /f /im explorer.exe & start explorer.exe & echo Explorer restarted."),
    };
}
