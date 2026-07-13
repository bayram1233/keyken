using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Tools page. Bundles inventory views (programs, services, network) and quick
/// launchers for built-in Windows consoles, plus report generation, restore
/// point management and the hosts file editor.
/// </summary>
public sealed partial class ToolsViewModel : ViewModelBase
{
    private readonly IToolsService _tools;
    private readonly IReportService _reports;
    private readonly IRestorePointService _restore;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;

    public ObservableCollection<InstalledProgram> Programs { get; } = new();
    public ObservableCollection<ServiceItem> Services { get; } = new();
    public ObservableCollection<NetworkAdapterInfo> Adapters { get; } = new();

    [ObservableProperty] private string _ipConfig = string.Empty;
    [ObservableProperty] private string _hostsContent = string.Empty;

    public ToolsViewModel(IToolsService tools, IReportService reports, IRestorePointService restore,
        IDialogService dialog, INotificationService notify)
    {
        Title = "Tools";
        _tools = tools;
        _reports = reports;
        _restore = restore;
        _dialog = dialog;
        _notify = notify;
    }

    public override async Task OnActivatedAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading system inventory...";
        try
        {
            Programs.Clear();
            foreach (var p in await _tools.GetInstalledProgramsAsync()) Programs.Add(p);

            Services.Clear();
            foreach (var s in await _tools.GetServicesAsync()) Services.Add(s);

            Adapters.Clear();
            foreach (var a in await _tools.GetNetworkAdaptersAsync()) Adapters.Add(a);

            IpConfig = await _tools.GetIpConfigAsync();
            HostsContent = await _tools.ReadHostsFileAsync();
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private void Launch(string tool) => _tools.LaunchSystemTool(tool);

    [RelayCommand]
    private async Task SaveHosts()
    {
        if (!_dialog.Confirm("Save hosts file", "Overwrite the system hosts file? A backup will be created."))
            return;
        await _tools.WriteHostsFileAsync(HostsContent);
        _notify.ShowSuccess("Hosts saved", "The hosts file was updated.");
    }

    [RelayCommand]
    private async Task CreateRestorePoint()
    {
        IsBusy = true;
        StatusMessage = "Creating restore point...";
        try
        {
            var ok = await _restore.CreateAsync("Fendr System Care - manual restore point");
            if (ok) _notify.ShowSuccess("Restore point created", "You can roll back to this point later.");
            else _notify.ShowWarning("Restore point", "Could not create a restore point (is protection enabled?).");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private void OpenRestoreManager() => _tools.LaunchSystemTool("rstrui.exe");

    [RelayCommand]
    private async Task GenerateReport(string format)
    {
        var folder = _dialog.PickFolder("Choose where to save the report");
        if (folder is null) return;

        var reportFormat = format switch
        {
            "html" => ReportFormat.Html,
            "pdf" => ReportFormat.Pdf,
            _ => ReportFormat.Json
        };

        IsBusy = true;
        StatusMessage = "Generating report...";
        try
        {
            var path = await _reports.GenerateAsync(reportFormat, folder);
            _notify.ShowSuccess("Report generated", path);
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }
}
