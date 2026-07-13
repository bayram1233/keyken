using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using FendrSystemCare.ViewModels;
using FendrSystemCare.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace FendrSystemCare;

/// <summary>
/// Application entry point. Builds the dependency-injection container using the
/// generic host, wires up every service, view-model and view, then shows the
/// main shell window. All long-running work is delegated to asynchronous
/// services resolved from this container.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;

    /// <summary>
    /// Provides ambient access to the DI container for the rare cases where a
    /// view needs to resolve its data-context (WPF instantiates views via XAML).
    /// </summary>
    public static IServiceProvider Services { get; private set; } = default!;

    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((_, services) => ConfigureServices(services))
            .Build();

        Services = _host.Services;

        // Catch exceptions from every source so the app never hard-crashes.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>
    /// Registers every dependency. Services are singletons because they are
    /// stateless helpers or hold monitoring state that must be shared; the shell
    /// view-model is a singleton while page view-models are transient so they
    /// refresh their data each time the user navigates to them.
    /// </summary>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Infrastructure services.
        services.AddSingleton<ILoggingService, LoggingService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INotificationService, NotificationService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IRestorePointService, RestorePointService>();
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<ILicenseService, LicenseService>();

        // Feature services.
        services.AddSingleton<ISystemInfoService, SystemInfoService>();
        services.AddSingleton<IMonitoringService, MonitoringService>();
        services.AddSingleton<ICleanupService, CleanupService>();
        services.AddSingleton<IRepairService, RepairService>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<IPerformanceService, PerformanceService>();
        services.AddSingleton<IDriverService, DriverService>();
        services.AddSingleton<IDriverCenterService, DriverCenterService>();
        services.AddSingleton<IWindowsUpdateService, WindowsUpdateService>();
        services.AddSingleton<IToolsService, ToolsService>();
        services.AddSingleton<IReportService, ReportService>();
        services.AddSingleton<IDashboardService, DashboardService>();

        // Shell + page view-models.
        services.AddSingleton<MainViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<DriverCenterViewModel>();
        services.AddTransient<SystemRepairViewModel>();
        services.AddTransient<CleanupViewModel>();
        services.AddTransient<PerformanceViewModel>();
        services.AddTransient<StartupViewModel>();
        services.AddTransient<WindowsUpdateViewModel>();
        services.AddTransient<ToolsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<AboutViewModel>();

        // Main shell window.
        services.AddSingleton<MainWindow>();
    }

    /// <summary>
    /// Starts the host, applies the persisted theme, and shows the main window.
    /// </summary>
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // Defensive elevation check: the manifest requests admin, but if the app
        // is ever launched unelevated, offer to relaunch with the rights it needs.
        if (!AdminHelper.IsAdministrator())
        {
            var choice = MessageBox.Show(
                "Fendr System Care needs administrator rights to perform maintenance.\n\nRestart as administrator now?",
                "Administrator required", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (choice == MessageBoxResult.Yes && AdminHelper.TryRelaunchAsAdministrator())
            {
                Shutdown();
                return;
            }
        }

        await _host.StartAsync();

        // Apply the saved theme + accent before any window becomes visible.
        var settings = _host.Services.GetRequiredService<ISettingsService>();
        await settings.LoadAsync();
        _host.Services.GetRequiredService<IThemeService>().Apply(settings.Current.Theme, settings.Current.AccentColor);

        var license = _host.Services.GetRequiredService<ILicenseService>();
        if (!license.IsLicensed)
        {
            var licensed = false;
            var licenseVm = new LicenseViewModel(license, () => licensed = true);
            var licenseWindow = new LicenseWindow(licenseVm);
            licenseWindow.ShowDialog();
            if (!licensed && !license.IsLicensed)
            {
                Shutdown();
                return;
            }
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        window.Show();
    }

    /// <summary>
    /// Logs and reports any unhandled UI-thread exception instead of crashing,
    /// keeping the application resilient during maintenance operations.
    /// </summary>
    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            _host.Services.GetRequiredService<ILoggingService>()
                .Error("Unhandled UI exception", e.Exception);
            _host.Services.GetRequiredService<INotificationService>()
                .ShowError("Unexpected error", e.Exception.Message);
        }
        catch
        {
            // Never let the crash handler itself crash the process.
        }

        e.Handled = true;
    }

    /// <summary>Logs non-UI-thread exceptions that would otherwise crash the process.</summary>
    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            TryLog("Unhandled application exception", ex);
    }

    /// <summary>Observes and logs faulted tasks so they do not tear down the app.</summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        TryLog("Unobserved task exception", e.Exception);
        e.SetObserved();
    }

    /// <summary>Best-effort logging that never throws from a crash handler.</summary>
    private void TryLog(string message, Exception ex)
    {
        try { _host.Services.GetRequiredService<ILoggingService>().Error(message, ex); }
        catch { /* Nothing more we can safely do here. */ }
    }

    /// <summary>
    /// Gracefully stops background services and the host on shutdown.
    /// </summary>
    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            _host.Services.GetRequiredService<IMonitoringService>().Stop();
            await _host.StopAsync(TimeSpan.FromSeconds(3));
            _host.Dispose();
        }
        catch
        {
            // Shutdown must always complete.
        }

        base.OnExit(e);
    }
}
