using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>Sürücü Merkezi — güvenli sürücü yönetimi kontrol paneli.</summary>
public sealed partial class DriverCenterViewModel : ViewModelBase
{
    private readonly IDriverCenterService _center;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;
    private CancellationTokenSource? _cts;

    public ObservableCollection<DriverDevice> Devices { get; } = new();
    public ObservableCollection<DriverStorePackage> StorePackages { get; } = new();
    public ICollectionView DeviceView { get; }

    [ObservableProperty] private DriverCenterStats _stats = new();
    [ObservableProperty] private DriverDevice? _selectedDevice;
    [ObservableProperty] private DriverDeviceFilter _activeFilter = DriverDeviceFilter.All;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private double _scanProgress;
    [ObservableProperty] private int _deviceCount;

    public bool HasSelection => SelectedDevice is not null;

    public DriverCenterViewModel(IDriverCenterService center, IDialogService dialog, INotificationService notify)
    {
        Title = "Sürücü Merkezi";
        _center = center;
        _dialog = dialog;
        _notify = notify;
        DeviceView = CollectionViewSource.GetDefaultView(Devices);
        DeviceView.Filter = FilterDevice;
        DeviceView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(DriverDevice.Category)));
    }

    public override async Task OnActivatedAsync() => await FullScanAsync();

    partial void OnSearchTextChanged(string value) => DeviceView.Refresh();
    partial void OnActiveFilterChanged(DriverDeviceFilter value) => DeviceView.Refresh();

    partial void OnSelectedDeviceChanged(DriverDevice? value)
    {
        OnPropertyChanged(nameof(HasSelection));
        _ = LoadDeviceDetailsAsync(value);
    }

    private async Task LoadDeviceDetailsAsync(DriverDevice? device)
    {
        if (device is null || string.IsNullOrWhiteSpace(device.DeviceInstanceId)) return;
        try
        {
            var detailed = await _center.GetDeviceDetailsAsync(device.DeviceInstanceId);
            if (detailed is null) return;
            var idx = Devices.IndexOf(device);
            if (idx >= 0) Devices[idx] = detailed;
            SelectedDevice = detailed;
        }
        catch { /* Detay yüklenemezse listedeki özet yeterli. */ }
    }

    [RelayCommand]
    private async Task FullScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        ScanProgress = 5;
        StatusMessage = "Donanım taraması başlatılıyor...";

        var progress = new Progress<string>(m =>
        {
            StatusMessage = m;
            ScanProgress = Math.Min(ScanProgress + 3, 92);
        });

        try
        {
            Devices.Clear();
            DeviceView.Refresh();

            var list = await _center.ScanAllDevicesAsync(progress, token);
            token.ThrowIfCancellationRequested();

            foreach (var d in list)
                Devices.Add(d);

            DeviceCount = list.Count;
            Stats = _center.ComputeStats(list);
            DeviceView.Refresh();

            ScanProgress = 100;
            _notify.ShowSuccess("Tarama tamamlandı", $"{list.Count} cihaz bulundu.");
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Tarama iptal edildi.";
            _notify.ShowWarning("İptal", "Donanım taraması iptal edildi.");
        }
        catch (Exception ex)
        {
            _notify.ShowError("Tarama hatası", ex.Message);
            StatusMessage = $"Hata: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            if (ScanProgress < 100 && Devices.Count > 0) ScanProgress = 100;
            if (string.IsNullOrEmpty(StatusMessage) || StatusMessage.StartsWith("Hata"))
                { /* keep */ }
            else
                StatusMessage = Devices.Count > 0
                    ? $"{Devices.Count} cihaz listelendi."
                    : string.Empty;
        }
    }

  /// <summary>
    /// Güncelle: otomatik indirme yok — resmi Windows güncelleme akışını açar.
    /// </summary>
    [RelayCommand]
    private void UpdateDriver()
    {
        const string msg =
            "Fendr System Care bilinmeyen kaynaklardan sürücü İNDİRMEZ ve otomatik kurulum yapmaz.\n\n" +
            "Güvenli güncelleme seçenekleri:\n" +
            "• Evet → Windows Update (isteğe bağlı sürücüler)\n" +
            "• Hayır → Microsoft Update Catalog (seçili cihaz HW ID)\n" +
            "• İptal → Aygıt Yöneticisi";

        var result = MessageBox.Show(msg, "Sürücü Güncelle",
            MessageBoxButton.YesNoCancel, MessageBoxImage.Information);

        switch (result)
        {
            case MessageBoxResult.Yes:
                _center.OpenOptionalDriverUpdates();
                _notify.ShowInfo("Windows Update",
                    "Windows Update isteğe bağlı sürücüler sayfası açıldı. Güncellemeleri oradan onaylayın.");
                break;

            case MessageBoxResult.No:
                if (SelectedDevice is { HardwareId: { Length: > 0 } hw })
                {
                    _center.OpenMicrosoftUpdateCatalog(hw);
                    _notify.ShowInfo("Update Catalog",
                        $"'{SelectedDevice.DeviceName}' için Microsoft Update Catalog açıldı.");
                }
                else
                {
                    _notify.ShowWarning("Cihaz seçin",
                        "Catalog araması için listeden bir cihaz seçin, sonra tekrar deneyin.");
                }
                break;

            case MessageBoxResult.Cancel:
                _center.OpenDeviceManager();
                _notify.ShowInfo("Aygıt Yöneticisi", "devmgmt.msc açıldı.");
                break;
        }
    }

    [RelayCommand]
    private async Task ScanWindowsUpdatesAsync()
    {
        if (Devices.Count == 0)
        {
            await FullScanAsync();
            if (Devices.Count == 0) return;
        }

        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = "Windows Update sürücüleri taranıyor...";
        try
        {
            var count = await _center.ScanWindowsUpdateDriversAsync(
                Devices, new Progress<string>(m => StatusMessage = m), _cts.Token);
            Stats = _center.ComputeStats(Devices.ToList());
            DeviceView.Refresh();
            _notify.ShowInfo("WU taraması",
                count > 0
                    ? $"{count} sürücü güncellemesi bulundu. 'Güncelle' ile Windows Update açın."
                    : "Windows Update üzerinde yeni sürücü bulunamadı.");
        }
        catch (Exception ex) { _notify.ShowError("WU taraması", ex.Message); }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task BackupAllAsync()
    {
        var folder = _dialog.PickFolder("Sürücü yedek klasörü seçin");
        if (folder is null) return;
        if (!_dialog.Confirm("Yedekleme", "Tüm sürücüler yedeklenecek. Geri yükleme noktası oluşturulacak. Devam?")) return;
        IsBusy = true;
        try
        {
            var ok = await _center.BackupAllAsync(folder, new Progress<string>(m => StatusMessage = m));
            if (ok)
            {
                _notify.ShowSuccess("Yedekleme", "Sürücüler başarıyla yedeklendi.");
                Stats = _center.ComputeStats(Devices.ToList());
            }
            else _notify.ShowWarning("Yedekleme", "Yedekleme tamamlanamadı.");
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task BackupSelectedAsync()
    {
        if (SelectedDevice is null) return;
        var folder = _dialog.PickFolder($"Yedek: {SelectedDevice.DeviceName}");
        if (folder is null) return;
        IsBusy = true;
        try
        {
            var ok = await _center.BackupSelectedAsync(new[] { SelectedDevice }, folder, new Progress<string>(m => StatusMessage = m));
            if (ok) _notify.ShowSuccess("Yedekleme", SelectedDevice.DeviceName);
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        var folder = _dialog.PickFolder("Geri yüklenecek .inf dosyasının klasörünü seçin");
        if (folder is null) return;
        var inf = Directory.EnumerateFiles(folder, "*.inf", SearchOption.AllDirectories).FirstOrDefault();
        if (inf is null) { _notify.ShowWarning("Geri yükleme", "INF dosyası bulunamadı."); return; }
        if (!_dialog.Confirm("Geri Yükleme", $"Sürücü geri yüklenecek:\n{inf}\n\nGeri yükleme noktası oluşturulacak.")) return;
        IsBusy = true;
        try
        {
            var ok = await _center.RestoreFromBackupAsync(inf, new Progress<string>(m => StatusMessage = m));
            if (ok)
            {
                _notify.ShowSuccess("Geri yükleme", "Sürücü geri yüklendi.");
                Stats = _center.ComputeStats(Devices.ToList());
            }
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task ExportReportAsync(string format)
    {
        var folder = _dialog.PickFolder("Rapor kayıt klasörü");
        if (folder is null) return;
        IsBusy = true;
        try
        {
            var path = await _center.ExportReportAsync(folder, format, Devices.ToList());
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            _notify.ShowSuccess("Dışa aktarma", path);
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task ToggleDeviceAsync(string? enableText)
    {
        if (SelectedDevice is null) return;
        var enable = string.Equals(enableText, "True", StringComparison.OrdinalIgnoreCase);
        var action = enable ? "etkinleştirmek" : "devre dışı bırakmak";
        if (!_dialog.Confirm("Cihaz", $"'{SelectedDevice.DeviceName}' cihazını {action} istiyor musunuz?")) return;
        IsBusy = true;
        try
        {
            await _center.SetDeviceEnabledAsync(SelectedDevice, enable);
            await FullScanAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand] private void OpenDeviceManager() => _center.OpenDeviceManager();
    [RelayCommand] private async Task RescanHardwareAsync() { await _center.RescanHardwareAsync(); await FullScanAsync(); }
    [RelayCommand] private void CancelScan() => _cts?.Cancel();
    [RelayCommand] private void SetFilter(string? f) { if (Enum.TryParse<DriverDeviceFilter>(f, out var filter)) ActiveFilter = filter; }

    [RelayCommand]
    private async Task ShowDeviceDetailsAsync()
    {
        if (SelectedDevice is null) return;
        await LoadDeviceDetailsAsync(SelectedDevice);
    }

    private bool FilterDevice(object obj)
    {
        if (obj is not DriverDevice d) return false;
        if (!string.IsNullOrWhiteSpace(SearchText))
        {
            var q = SearchText;
            if (!d.DeviceName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !d.Manufacturer.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !d.DriverVersion.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !d.HardwareId.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !d.Provider.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                !d.DeviceClass.Contains(q, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return ActiveFilter switch
        {
            DriverDeviceFilter.Problem => d.HasProblem,
            DriverDeviceFilter.Missing => d.IsMissing,
            DriverDeviceFilter.Unsigned => !d.IsSigned && !d.IsUnknown,
            DriverDeviceFilter.Outdated => d.IsOutdated,
            DriverDeviceFilter.Disabled => !d.IsEnabled,
            DriverDeviceFilter.Unknown => d.IsUnknown,
            DriverDeviceFilter.RecentlyInstalled => d.DriverDate is { } dt && dt > DateTime.Now.AddDays(-30),
            DriverDeviceFilter.OldDrivers => d.DriverDate is { } dt2 && dt2 < DateTime.Now.AddYears(-3),
            _ => true
        };
    }
}
