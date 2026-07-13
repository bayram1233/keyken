using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>
/// Provides system inventory queries (installed programs/services/adapters) and
/// launches built-in Windows management consoles. The hosts file editor reads
/// and writes %windir%\System32\drivers\etc\hosts.
/// </summary>
public sealed class ToolsService : IToolsService
{
    private static readonly string HostsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");

    private readonly ILoggingService _log;

    public ToolsService(ILoggingService log) => _log = log;

    public Task<IReadOnlyList<InstalledProgram>> GetInstalledProgramsAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<InstalledProgram>>(() =>
        {
            var programs = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);
            ReadUninstall(Registry.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", programs);
            ReadUninstall(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall", programs);
            ReadUninstall(Registry.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", programs);
            return programs.Values.OrderBy(p => p.Name).ToList();
        }, ct);

    public Task<IReadOnlyList<ServiceItem>> GetServicesAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<ServiceItem>>(() =>
        {
            var services = new List<ServiceItem>();
            try
            {
                using var searcher = new ManagementObjectSearcher(
                    "SELECT Name, DisplayName, State, StartMode FROM Win32_Service");
                foreach (var o in searcher.Get())
                {
                    ct.ThrowIfCancellationRequested();
                    services.Add(new ServiceItem
                    {
                        Name = o["Name"]?.ToString() ?? string.Empty,
                        DisplayName = o["DisplayName"]?.ToString() ?? string.Empty,
                        Status = o["State"]?.ToString() ?? string.Empty,
                        StartType = o["StartMode"]?.ToString() ?? string.Empty
                    });
                }
            }
            catch (Exception ex) { _log.Error("Failed to enumerate services.", ex); }
            return services.OrderBy(s => s.DisplayName).ToList();
        }, ct);

    public Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<NetworkAdapterInfo>>(() =>
        {
            var adapters = new List<NetworkAdapterInfo>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = nic.GetIPProperties();
                adapters.Add(new NetworkAdapterInfo
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    MacAddress = FormatMac(nic.GetPhysicalAddress().GetAddressBytes()),
                    Ipv4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "-",
                    Ipv6 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)?.Address.ToString() ?? "-",
                    Gateway = props.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "-",
                    Status = nic.OperationalStatus.ToString()
                });
            }
            return adapters;
        }, ct);

    public Task<string> GetIpConfigAsync(CancellationToken ct = default) =>
        ProcessRunner.CaptureAsync("ipconfig", "/all", ct);

    public void LaunchSystemTool(string tool)
    {
        // Map friendly names to the actual console/executable to launch.
        var target = tool switch
        {
            "device_manager" => "devmgmt.msc",
            "event_viewer" => "eventvwr.msc",
            "task_scheduler" => "taskschd.msc",
            "services" => "services.msc",
            "system_info" => "msinfo32.exe",
            "disk_management" => "diskmgmt.msc",
            "computer_management" => "compmgmt.msc",
            "registry_editor" => "regedit.exe",
            "environment_variables" => "rundll32.exe",
            _ => tool
        };

        try
        {
            var psi = target == "rundll32.exe"
                ? new ProcessStartInfo(target, "sysdm.cpl,EditEnvironmentVariables") { UseShellExecute = true }
                : new ProcessStartInfo(target) { UseShellExecute = true };
            Process.Start(psi);
            _log.Info($"Launched system tool: {tool}");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed to launch '{tool}'.", ex);
        }
    }

    public async Task<string> ReadHostsFileAsync(CancellationToken ct = default)
    {
        try { return await File.ReadAllTextAsync(HostsPath, ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.Error("Failed to read hosts file.", ex); return string.Empty; }
    }

    public async Task WriteHostsFileAsync(string content, CancellationToken ct = default)
    {
        try
        {
            // Keep a timestamped backup before overwriting this sensitive file.
            if (File.Exists(HostsPath))
                File.Copy(HostsPath, HostsPath + $".bak-{DateTime.Now:yyyyMMddHHmmss}", overwrite: true);
            await File.WriteAllTextAsync(HostsPath, content, ct).ConfigureAwait(false);
            _log.Success("Hosts file saved.");
        }
        catch (Exception ex) { _log.Error("Failed to write hosts file.", ex); }
    }

    // ----- helpers ----------------------------------------------------------

    private void ReadUninstall(RegistryKey root, string path, Dictionary<string, InstalledProgram> into)
    {
        try
        {
            using var key = root.OpenSubKey(path);
            if (key is null) return;
            foreach (var sub in key.GetSubKeyNames())
            {
                using var app = key.OpenSubKey(sub);
                var name = app?.GetValue("DisplayName")?.ToString();
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (app?.GetValue("SystemComponent") is int sc && sc == 1) continue;

                into[name] = new InstalledProgram
                {
                    Name = name,
                    Version = app?.GetValue("DisplayVersion")?.ToString() ?? string.Empty,
                    Publisher = app?.GetValue("Publisher")?.ToString() ?? string.Empty,
                    InstallDate = app?.GetValue("InstallDate")?.ToString() ?? string.Empty,
                    UninstallString = app?.GetValue("UninstallString")?.ToString()
                };
            }
        }
        catch (Exception ex) { _log.Warning($"Failed to read {path}.", ex.Message); }
    }

    private static string FormatMac(byte[] bytes) =>
        bytes.Length == 0 ? "-" : string.Join(":", bytes.Select(b => b.ToString("X2")));
}
