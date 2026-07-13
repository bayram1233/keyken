using FendrSystemCare.Models;

namespace FendrSystemCare.Services.Interfaces;

/// <summary>Queries static hardware/OS information and computes a health score.</summary>
public interface ISystemInfoService
{
    Task<SystemInformation> GetSystemInformationAsync(CancellationToken ct = default);
    Task<IReadOnlyList<StorageVolume>> GetStorageVolumesAsync(CancellationToken ct = default);
    Task<HealthScore> ComputeHealthScoreAsync(CancellationToken ct = default);
    Task<TimeSpan> GetUptimeAsync(CancellationToken ct = default);
    Task<double> MeasureInternetSpeedMbpsAsync(CancellationToken ct = default);
}

/// <summary>Emits live CPU/RAM/GPU/disk samples on a timer.</summary>
public interface IMonitoringService
{
    event EventHandler<PerformanceSample>? SampleAvailable;
    PerformanceSample Latest { get; }
    void Start();
    void Stop();
}

/// <summary>Scans and cleans temporary/cache locations.</summary>
public interface ICleanupService
{
    IReadOnlyList<CleanupCategory> GetCategories();
    Task ScanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default);
    Task<long> CleanAsync(IEnumerable<CleanupCategory> categories, IProgress<string>? progress, CancellationToken ct = default);
}

/// <summary>Runs Windows repair tooling (SFC, DISM, CHKDSK, network resets, etc.).</summary>
public interface IRepairService
{
    IReadOnlyList<RepairTask> GetTasks();

    /// <summary>Executes a single task, streaming console output via <paramref name="output"/>.</summary>
    Task<RepairResult> RunAsync(RepairTask task, IProgress<string>? output, CancellationToken ct = default);
}

/// <summary>Reads and modifies logon startup entries.</summary>
public interface IStartupService
{
    Task<IReadOnlyList<StartupItem>> GetStartupItemsAsync(CancellationToken ct = default);
    Task SetEnabledAsync(StartupItem item, bool enabled, CancellationToken ct = default);
    Task RemoveAsync(StartupItem item, CancellationToken ct = default);
    Task SetDelayAsync(StartupItem item, int seconds, CancellationToken ct = default);
}

/// <summary>Applies performance/power tweaks.</summary>
public interface IPerformanceService
{
    Task OptimizeServicesAsync(IProgress<string>? progress, CancellationToken ct = default);
    Task DisableTelemetryAsync(CancellationToken ct = default);
    Task DisableBackgroundAppsAsync(CancellationToken ct = default);
    Task OptimizeDrivesAsync(IProgress<string>? progress, CancellationToken ct = default);
    Task EnableGamingModeAsync(CancellationToken ct = default);
    Task<long> CleanMemoryAsync(CancellationToken ct = default);
    Task SetHighPerformancePowerPlanAsync(CancellationToken ct = default);
}

/// <summary>Enumerates and services device drivers via pnputil/WMI.</summary>
public interface IDriverService
{
    Task<IReadOnlyList<DriverInfo>> GetInstalledDriversAsync(CancellationToken ct = default);
    Task<IReadOnlyList<DriverInfo>> DetectProblemDevicesAsync(CancellationToken ct = default);
    Task ScanForUpdatesAsync(IEnumerable<DriverInfo> drivers, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> BackupDriversAsync(string destinationFolder, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> RestoreDriverAsync(string infPath, CancellationToken ct = default);
    Task<bool> ExportDriverAsync(DriverInfo driver, string destinationFolder, CancellationToken ct = default);
    Task<bool> UpdateDriverAsync(DriverInfo driver, IProgress<string>? progress, CancellationToken ct = default);
}

/// <summary>Profesyonel sürücü merkezi — tarama, sağlık, yedekleme, cihaz yönetimi.</summary>
public interface IDriverCenterService
{
    Task<DriverCenterStats> GetStatsAsync(CancellationToken ct = default);
    DriverCenterStats ComputeStats(IReadOnlyList<DriverDevice> devices);
    Task<IReadOnlyList<DriverDevice>> ScanAllDevicesAsync(IProgress<string>? progress, CancellationToken ct = default);
    Task<DriverDevice?> GetDeviceDetailsAsync(string deviceInstanceId, CancellationToken ct = default);
    Task<int> ScanWindowsUpdateDriversAsync(IList<DriverDevice> devices, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> BackupAllAsync(string folder, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> BackupSelectedAsync(IEnumerable<DriverDevice> devices, string folder, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> RestoreFromBackupAsync(string infPath, IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> ExportDriverAsync(DriverDevice device, string folder, CancellationToken ct = default);
    Task<string> ExportReportAsync(string folder, string format, IReadOnlyList<DriverDevice> devices, CancellationToken ct = default);
    Task<IReadOnlyList<DriverStorePackage>> GetDriverStoreAsync(CancellationToken ct = default);
    Task<bool> CleanupUnusedDriversAsync(IProgress<string>? progress, CancellationToken ct = default);
    Task<bool> SetDeviceEnabledAsync(DriverDevice device, bool enabled, CancellationToken ct = default);
    Task RescanHardwareAsync(CancellationToken ct = default);
    void OpenDeviceManager();
    void OpenWindowsUpdateDrivers();
    void OpenOptionalDriverUpdates();
    void OpenMicrosoftUpdateCatalog(string hardwareId);
    IReadOnlyList<DriverBackupRecord> GetBackupHistory();
}

/// <summary>Wraps the Windows Update agent.</summary>
public interface IWindowsUpdateService
{
    Task<IReadOnlyList<WindowsUpdateItem>> CheckForUpdatesAsync(CancellationToken ct = default);
    Task InstallUpdatesAsync(IProgress<string>? progress, CancellationToken ct = default);
    Task<IReadOnlyList<WindowsUpdateItem>> GetHistoryAsync(CancellationToken ct = default);
    Task PauseUpdatesAsync(int days, CancellationToken ct = default);
    Task ResumeUpdatesAsync(CancellationToken ct = default);
}

/// <summary>System tool shortcuts and inventory queries.</summary>
public interface IToolsService
{
    Task<IReadOnlyList<InstalledProgram>> GetInstalledProgramsAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ServiceItem>> GetServicesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<NetworkAdapterInfo>> GetNetworkAdaptersAsync(CancellationToken ct = default);
    Task<string> GetIpConfigAsync(CancellationToken ct = default);
    void LaunchSystemTool(string tool);
    Task<string> ReadHostsFileAsync(CancellationToken ct = default);
    Task WriteHostsFileAsync(string content, CancellationToken ct = default);
}

/// <summary>Generates JSON/HTML/PDF reports of the current system state.</summary>
public interface IReportService
{
    Task<string> GenerateAsync(ReportFormat format, string destinationFolder, CancellationToken ct = default);
}

/// <summary>
/// Dashboard kontrol paneli servisi. Donanım ve sistem verilerini toplar,
/// canlı güncellemeleri yönetir ve uyarı/performans skorlarını hesaplar.
/// </summary>
public interface IDashboardService
{
    /// <summary>Statik dashboard verisini yükler (önbellekli).</summary>
    Task<DashboardSnapshot> LoadSnapshotAsync(CancellationToken ct = default);

    /// <summary>Canlı izlemeyi başlatır.</summary>
    void StartLiveMonitoring();

    /// <summary>Canlı izlemeyi durdurur.</summary>
    void StopLiveMonitoring();

    /// <summary>Canlı izlemeyi duraklatır/devam ettirir.</summary>
    void SetLivePaused(bool paused);

    /// <summary>Dashboard düzen ayarlarını yükler.</summary>
    Task<DashboardLayoutSettings> LoadLayoutAsync(CancellationToken ct = default);

    /// <summary>Dashboard düzen ayarlarını kaydeder.</summary>
    Task SaveLayoutAsync(DashboardLayoutSettings layout, CancellationToken ct = default);

    /// <summary>Düzeni varsayılana sıfırlar.</summary>
    Task ResetLayoutAsync(CancellationToken ct = default);

    /// <summary>En son canlı örnek.</summary>
    PerformanceSample LatestSample { get; }

    /// <summary>Her saniye yeni canlı örnek geldiğinde tetiklenir.</summary>
    event EventHandler<PerformanceSample>? LiveSampleAvailable;
}
