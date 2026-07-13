using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Performance page. Shows live CPU/RAM/GPU/disk graphs and exposes one-click
/// optimisation actions (services, telemetry, background apps, drive optimise,
/// gaming mode, memory cleaner, high-performance power plan).
/// </summary>
public sealed partial class PerformanceViewModel : ViewModelBase, IDisposable
{
    private const int HistoryLength = 60;
    private const double GraphWidth = 560;
    private const double GraphHeight = 120;

    private readonly IPerformanceService _performance;
    private readonly IMonitoringService _monitoring;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;

    private readonly Queue<double> _cpu = new();
    private readonly Queue<double> _ram = new();
    private readonly Queue<double> _gpu = new();

    [ObservableProperty] private double _cpuPercent;
    [ObservableProperty] private double _ramPercent;
    [ObservableProperty] private double _gpuPercent;
    [ObservableProperty] private double _diskPercent;
    [ObservableProperty] private string _networkText = "-";
    [ObservableProperty] private string _temperatureText = "N/A";
    [ObservableProperty] private string _fanText = "N/A";

    [ObservableProperty] private PointCollection _cpuPoints = new();
    [ObservableProperty] private PointCollection _ramPoints = new();
    [ObservableProperty] private PointCollection _gpuPoints = new();

    public PerformanceViewModel(IPerformanceService performance, IMonitoringService monitoring,
        IDialogService dialog, INotificationService notify)
    {
        Title = "Performance";
        _performance = performance;
        _monitoring = monitoring;
        _dialog = dialog;
        _notify = notify;
        _monitoring.SampleAvailable += OnSample;
    }

    public override Task OnActivatedAsync()
    {
        _monitoring.Start();
        return Task.CompletedTask;
    }

    [RelayCommand]
    private Task OptimizeServices() => Run("Optimize services",
        p => _performance.OptimizeServicesAsync(p, CancellationToken.None), confirm: true);

    [RelayCommand]
    private Task DisableTelemetry() => Run("Disable telemetry",
        _ => _performance.DisableTelemetryAsync(), confirm: true);

    [RelayCommand]
    private Task DisableBackgroundApps() => Run("Disable background apps",
        _ => _performance.DisableBackgroundAppsAsync(), confirm: true);

    [RelayCommand]
    private Task OptimizeDrives() => Run("Optimize drives",
        p => _performance.OptimizeDrivesAsync(p, CancellationToken.None), confirm: false);

    [RelayCommand]
    private Task GamingMode() => Run("Enable gaming mode",
        _ => _performance.EnableGamingModeAsync(), confirm: false);

    [RelayCommand]
    private Task HighPerformance() => Run("High performance power plan",
        _ => _performance.SetHighPerformancePowerPlanAsync(), confirm: false);

    [RelayCommand]
    private async Task CleanMemory()
    {
        IsBusy = true;
        StatusMessage = "Trimming working sets...";
        try
        {
            var freed = await _performance.CleanMemoryAsync();
            _notify.ShowSuccess("Memory cleaned", $"Trimmed {Utilities.FormatHelper.Bytes(freed)}.");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    private async Task Run(string name, Func<IProgress<string>, Task> action, bool confirm)
    {
        if (confirm && !_dialog.Confirm("Confirm", $"Apply '{name}'? This changes system settings."))
            return;

        IsBusy = true;
        var progress = new Progress<string>(m => StatusMessage = m);
        try
        {
            await action(progress);
            _notify.ShowSuccess("Done", $"{name} applied.");
        }
        catch (Exception ex)
        {
            _notify.ShowError(name, ex.Message);
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    private void OnSample(object? sender, PerformanceSample sample)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            CpuPercent = sample.CpuPercent;
            RamPercent = sample.RamPercent;
            GpuPercent = sample.GpuPercent;
            DiskPercent = sample.DiskPercent;
            NetworkText = $"{sample.NetworkMbps:0.##} Mbps";
            TemperatureText = sample.TemperatureCelsius is { } t ? $"{t:0.#} °C" : "N/A";
            FanText = sample.FanRpm is { } rpm ? $"{rpm} RPM" : "N/A";

            Push(_cpu, sample.CpuPercent);
            Push(_ram, sample.RamPercent);
            Push(_gpu, sample.GpuPercent);

            CpuPoints = BuildPoints(_cpu);
            RamPoints = BuildPoints(_ram);
            GpuPoints = BuildPoints(_gpu);
        });
    }

    private static void Push(Queue<double> buffer, double value)
    {
        buffer.Enqueue(value);
        while (buffer.Count > HistoryLength) buffer.Dequeue();
    }

    private static PointCollection BuildPoints(Queue<double> buffer)
    {
        var values = buffer.ToArray();
        var points = new PointCollection();
        if (values.Length < 2) return points;

        var step = GraphWidth / (HistoryLength - 1);
        for (var i = 0; i < values.Length; i++)
        {
            var x = i * step;
            var y = GraphHeight - values[i] / 100.0 * GraphHeight;
            points.Add(new Point(x, y));
        }
        return points;
    }

    public void Dispose() => _monitoring.SampleAvailable -= OnSample;
}
