using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Dashboard kontrol paneli. Gerçek sistem verilerini gösterir, canlı grafikleri
/// günceller ve hızlı eylemler sunar.
/// </summary>
public sealed partial class DashboardViewModel : ViewModelBase, IDisposable
{
    private const int ChartHistory = 60;
    private const double ChartW = 280;
    private const double ChartH = 70;

    private readonly IDashboardService _dashboard;
    private readonly INavigationService _navigation;
    private readonly IRestorePointService _restore;
    private readonly IDriverService _drivers;
    private readonly IWindowsUpdateService _updates;
    private readonly IToolsService _tools;
    private readonly INotificationService _notify;
    private readonly IDialogService _dialog;

    private readonly Queue<double> _cpuHist = new();
    private readonly Queue<double> _gpuHist = new();
    private readonly Queue<double> _ramHist = new();
    private readonly Queue<double> _diskHist = new();
    private readonly Queue<double> _netHist = new();
    private readonly Queue<double> _tempHist = new();

    [ObservableProperty] private DashboardSnapshot? _snapshot;
    [ObservableProperty] private DashboardLayoutSettings _layout = new();
    [ObservableProperty] private bool _isLivePaused;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _liveStatus = "CANLI";

    // Canlı metrikler
    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private string _networkText = "-";
    [ObservableProperty] private string _temperatureText = "Yok";
    [ObservableProperty] private string _powerText = "-";

    // Grafikler
    [ObservableProperty] private PointCollection _cpuChart = new();
    [ObservableProperty] private PointCollection _gpuChart = new();
    [ObservableProperty] private PointCollection _ramChart = new();
    [ObservableProperty] private PointCollection _diskChart = new();
    [ObservableProperty] private PointCollection _netChart = new();
    [ObservableProperty] private PointCollection _tempChart = new();

    public ObservableCollection<DashboardSummaryCard> SummaryCards { get; } = new();
    public ObservableCollection<DashboardAlert> Alerts { get; } = new();
    public ObservableCollection<DashboardActivity> Activities { get; } = new();
    public ObservableCollection<DashboardDriveInfo> Drives { get; } = new();
    public ObservableCollection<string> HealthFindings { get; } = new();
    public ObservableCollection<string> PerformanceExplanations { get; } = new();

    public DashboardViewModel(IDashboardService dashboard, INavigationService navigation,
        IRestorePointService restore, IDriverService drivers, IWindowsUpdateService updates,
        IToolsService tools, INotificationService notify, IDialogService dialog)
    {
        Title = "Kontrol Paneli";
        _dashboard = dashboard;
        _navigation = navigation;
        _restore = restore;
        _drivers = drivers;
        _updates = updates;
        _tools = tools;
        _notify = notify;
        _dialog = dialog;
        _dashboard.LiveSampleAvailable += OnLiveSample;
    }

    public override async Task OnActivatedAsync()
    {
        IsLoading = true;
        StatusMessage = "Sistem verileri yükleniyor...";
        try
        {
            Layout = await _dashboard.LoadLayoutAsync();
            _dashboard.StartLiveMonitoring();

            Snapshot = await _dashboard.LoadSnapshotAsync();
            ApplySnapshot(Snapshot);
        }
        catch (Exception ex)
        {
            StatusMessage = "Veri yüklenemedi";
            _notify.ShowError("Dashboard", ex.Message);
        }
        finally
        {
            IsLoading = false;
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusMessage = "Yenileniyor...";
        try
        {
            Snapshot = await _dashboard.LoadSnapshotAsync();
            ApplySnapshot(Snapshot);
            _notify.ShowSuccess("Dashboard", "Veriler güncellendi.");
        }
        finally
        {
            IsBusy = false;
            StatusMessage = string.Empty;
        }
    }

    [RelayCommand]
    private void ToggleLivePause()
    {
        IsLivePaused = !IsLivePaused;
        _dashboard.SetLivePaused(IsLivePaused);
        LiveStatus = IsLivePaused ? "DURAKLATILDI" : "CANLI";
    }

    [RelayCommand]
    private async Task SaveLayoutAsync()
    {
        await _dashboard.SaveLayoutAsync(Layout);
        _notify.ShowSuccess("Düzen", "Dashboard düzeni kaydedildi.");
    }

    [RelayCommand]
    private async Task ResetLayoutAsync()
    {
        await _dashboard.ResetLayoutAsync();
        Layout = new DashboardLayoutSettings();
        _notify.ShowInfo("Düzen", "Varsayılan düzen geri yüklendi.");
    }

    // ----- Hızlı Eylemler ---------------------------------------------------

    [RelayCommand] private void GoDrivers() => _navigation.NavigateTo<DriverCenterViewModel>();
    [RelayCommand] private void GoCleanup() => _navigation.NavigateTo<CleanupViewModel>();
    [RelayCommand] private void GoRepair() => _navigation.NavigateTo<SystemRepairViewModel>();
    [RelayCommand] private void GoUpdates() => _navigation.NavigateTo<WindowsUpdateViewModel>();

    [RelayCommand]
    private async Task CreateRestorePointAsync()
    {
        if (!_dialog.Confirm("Geri Yükleme Noktası", "Geri yükleme noktası oluşturulsun mu?")) return;
        IsBusy = true;
        StatusMessage = "Geri yükleme noktası oluşturuluyor...";
        try
        {
            var ok = await _restore.CreateAsync("Fendr Dashboard");
            if (ok) _notify.ShowSuccess("Geri Yükleme", "Nokta başarıyla oluşturuldu.");
            else _notify.ShowWarning("Geri Yükleme", "Nokta oluşturulamadı.");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task ScanDriversAsync()
    {
        IsBusy = true;
        StatusMessage = "Sürücüler taranıyor...";
        try
        {
            var list = await _drivers.GetInstalledDriversAsync();
            await _drivers.ScanForUpdatesAsync(list, null);
            _notify.ShowSuccess("Sürücüler", "Tarama tamamlandı.");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task CheckUpdatesAsync()
    {
        IsBusy = true;
        StatusMessage = "Windows güncellemeleri kontrol ediliyor...";
        try
        {
            var updates = await _updates.CheckForUpdatesAsync();
            _notify.ShowInfo("Güncellemeler", $"{updates.Count} güncelleme bulundu.");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand] private void OpenTaskManager() => _tools.LaunchSystemTool("taskmgr");
    [RelayCommand] private void OpenDeviceManager() => _tools.LaunchSystemTool("devmgmt.msc");
    [RelayCommand] private void OpenServices() => _tools.LaunchSystemTool("services.msc");
    [RelayCommand] private void OpenEventViewer() => _tools.LaunchSystemTool("eventvwr.msc");

    [RelayCommand]
    private void RestartExplorer()
    {
        if (!_dialog.Confirm("Gezgin", "Windows Gezgini yeniden başlatılsın mı?")) return;
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
                p.Kill();
            Process.Start("explorer.exe");
            _notify.ShowSuccess("Gezgin", "Windows Gezgini yeniden başlatıldı.");
        }
        catch (Exception ex) { _notify.ShowError("Gezgin", ex.Message); }
    }

    // ----- Canlı grafik -----------------------------------------------------

    private void OnLiveSample(object? sender, PerformanceSample sample)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CpuPercent = sample.CpuPercent;
            RamPercent = sample.RamPercent;
            GpuPercent = sample.GpuPercent;
            DiskPercent = sample.DiskPercent;
            NetworkText = $"{sample.NetworkMbps:0.##} Mbps";
            TemperatureText = sample.TemperatureCelsius is { } t ? $"{t:0.#} °C" : "Yok";

            Push(_cpuHist, sample.CpuPercent); CpuChart = Build(_cpuHist);
            Push(_gpuHist, sample.GpuPercent); GpuChart = Build(_gpuHist);
            Push(_ramHist, sample.RamPercent); RamChart = Build(_ramHist);
            Push(_diskHist, sample.DiskPercent); DiskChart = Build(_diskHist);
            Push(_netHist, sample.NetworkMbps); NetChart = Build(_netHist, maxY: 100);
            var tempVal = sample.TemperatureCelsius ?? 0;
            Push(_tempHist, tempVal); TempChart = Build(_tempHist, maxY: 100);
        });
    }

    private void ApplySnapshot(DashboardSnapshot snap)
    {
        SummaryCards.Clear();
        foreach (var c in snap.SummaryCards) SummaryCards.Add(c);

        Alerts.Clear();
        foreach (var a in snap.Alerts) Alerts.Add(a);

        Activities.Clear();
        foreach (var a in snap.Activities) Activities.Add(a);

        Drives.Clear();
        foreach (var d in snap.Drives) Drives.Add(d);

        HealthFindings.Clear();
        foreach (var f in snap.Health.Findings) HealthFindings.Add(f);

        PerformanceExplanations.Clear();
        foreach (var e in snap.Performance.Explanations) PerformanceExplanations.Add(e);
    }

    private static void Push(Queue<double> q, double v)
    {
        q.Enqueue(v);
        while (q.Count > ChartHistory) q.Dequeue();
    }

    private static PointCollection Build(Queue<double> q, double maxY = 100)
    {
        var pts = new PointCollection();
        var arr = q.ToArray();
        if (arr.Length == 0) return pts;
        var step = ChartW / Math.Max(arr.Length - 1, 1);
        for (var i = 0; i < arr.Length; i++)
        {
            var y = ChartH - Math.Clamp(arr[i] / maxY, 0, 1) * ChartH;
            pts.Add(new Point(i * step, y));
        }
        return pts;
    }

    public void Dispose()
    {
        _dashboard.LiveSampleAvailable -= OnLiveSample;
        _dashboard.StopLiveMonitoring();
    }
}
