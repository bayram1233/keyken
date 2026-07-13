using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Management;
using System.Text;
using System.Text.Json;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Profesyonel sürücü merkezi servisi. Yalnızca resmi Windows araçları kullanır:
/// WMI, pnputil, SetupAPI (PowerShell PnpDevice), Windows Update COM.
/// </summary>
public sealed class DriverCenterService : IDriverCenterService
{
    private readonly IDriverService _drivers;
    private readonly IRestorePointService _restore;
    private readonly ILoggingService _log;

    private readonly string _metaPath;
    private DriverCenterMeta _meta = new();
    private readonly List<DriverBackupRecord> _backupHistory = new();

    public DriverCenterService(IDriverService drivers, IRestorePointService restore, ILoggingService log)
    {
        _drivers = drivers;
        _restore = restore;
        _log = log;
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FendrSystemCare");
        Directory.CreateDirectory(dir);
        _metaPath = Path.Combine(dir, "driver-center.json");
        LoadMeta();
    }

    public IReadOnlyList<DriverBackupRecord> GetBackupHistory() => _backupHistory;

    public async Task<DriverCenterStats> GetStatsAsync(CancellationToken ct = default)
    {
        var devices = await ScanAllDevicesAsync(null, ct).ConfigureAwait(false);
        return ComputeStats(devices);
    }

    public DriverCenterStats ComputeStats(IReadOnlyList<DriverDevice> devices) => BuildStats(devices);

    public Task<IReadOnlyList<DriverDevice>> ScanAllDevicesAsync(IProgress<string>? progress, CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DriverDevice>>(() =>
        {
            IOperationScope? scope = null;
            var sw = Stopwatch.StartNew();
            try
            {
                scope = _log.BeginOperation("Donanım taraması başladı");
                _log.Info("Tarama başlatıldı", DateTime.Now.ToString("O"));

                progress?.Report("PnP cihazları WMI ile taranıyor...");
                var signedIndex = BuildSignedDriverIndex();
                progress?.Report($"Sürücü envanteri yüklendi ({signedIndex.Count} kayıt).");

                var entities = QueryPnPEntities(progress, ct);
                progress?.Report($"{entities.Count} Plug and Play cihazı bulundu, birleştiriliyor...");

                var devices = new List<DriverDevice>(entities.Count);
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (var i = 0; i < entities.Count; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    var e = entities[i];
                    if (string.IsNullOrWhiteSpace(e.InstanceId) && string.IsNullOrWhiteSpace(e.Name))
                        continue;

                    var key = string.IsNullOrWhiteSpace(e.InstanceId)
                        ? e.Name
                        : e.InstanceId;
                    if (!seen.Add(key)) continue;

                    var match = FindSignedDriver(signedIndex, e.HardwareIds);
                    devices.Add(MergeDevice(e, match));

                    if (i % 25 == 0)
                        progress?.Report($"İşleniyor: {i + 1}/{entities.Count} — {e.Name}");
                }

                _meta.LastScan = DateTime.Now;
                SaveMeta();

                sw.Stop();
                _log.Success("Tarama tamamlandı",
                    $"Süre: {sw.ElapsedMilliseconds} ms, Cihaz: {devices.Count}");
                scope?.Complete($"{devices.Count} cihaz, {sw.ElapsedMilliseconds} ms");
                progress?.Report($"Tarama tamamlandı: {devices.Count} cihaz ({sw.Elapsed.TotalSeconds:0.0} sn).");
                return devices.OrderBy(d => d.Category).ThenBy(d => d.DeviceName).ToList();
            }
            catch (Exception ex)
            {
                sw.Stop();
                scope?.Fail(ex.Message, ex);
                _log.Error("Donanım taraması başarısız", ex);
                throw;
            }
        }, ct);

    public Task<DriverDevice?> GetDeviceDetailsAsync(string deviceInstanceId, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            var entity = QueryPnPEntities(null, ct).FirstOrDefault(e =>
                e.InstanceId.Equals(deviceInstanceId, StringComparison.OrdinalIgnoreCase));
            if (entity is null) return null;
            var index = BuildSignedDriverIndex();
            var match = FindSignedDriver(index, entity.HardwareIds);
            return MergeDevice(entity, match);
        }, ct);

    public async Task<int> ScanWindowsUpdateDriversAsync(IList<DriverDevice> devices, IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("Windows Update sürücü paketleri sorgulanıyor...");
        var legacy = devices.Select(ToLegacy).ToList();
        await _drivers.ScanForUpdatesAsync(legacy, progress, ct).ConfigureAwait(false);

        var count = 0;
        foreach (var d in devices)
        {
            var m = legacy.FirstOrDefault(l =>
                string.Equals(l.DeviceName, d.DeviceName, StringComparison.OrdinalIgnoreCase) ||
                (!string.IsNullOrEmpty(l.HardwareId) && l.HardwareId == d.HardwareId));
            if (m is null) continue;
            d.IsOutdated = m.IsOutdated;
            d.LatestVersion = m.LatestVersion;
            if (m.IsOutdated) count++;
        }

        _meta.LastScan = DateTime.Now;
        SaveMeta();
        _log.Info("Windows Update sürücü taraması", $"{count} güncelleme bulundu.");
        return count;
    }

    public async Task<bool> BackupAllAsync(string folder, IProgress<string>? progress, CancellationToken ct = default)
    {
        using var scope = _log.BeginOperation("Tüm sürücü yedekleme", folder);
        if (!await _restore.CreateAsync("Fendr - sürücü yedekleme öncesi", ct).ConfigureAwait(false))
        {
            scope.Fail("Geri yükleme noktası oluşturulamadı.");
            return false;
        }

        var ok = await _drivers.BackupDriversAsync(folder, progress, ct).ConfigureAwait(false);
        if (ok)
        {
            var zip = folder + ".zip";
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(folder, zip);
            var size = new FileInfo(zip).Length;
            var record = new DriverBackupRecord
            {
                Date = DateTime.Now,
                Path = zip,
                SizeBytes = size,
                DriverCount = Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories).Length,
                Verified = Directory.GetFiles(folder, "*.inf", SearchOption.AllDirectories).Length > 0
            };
            _backupHistory.Insert(0, record);
            _meta.LastBackup = DateTime.Now;
            _meta.BackupPath = zip;
            SaveMeta();
            scope.Complete();
            return true;
        }
        scope.Fail("Yedekleme başarısız.");
        return false;
    }

    public async Task<bool> BackupSelectedAsync(IEnumerable<DriverDevice> devices, string folder, IProgress<string>? progress, CancellationToken ct = default)
    {
        Directory.CreateDirectory(folder);
        var ok = true;
        foreach (var d in devices)
        {
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(d.InfName)) continue;
            progress?.Report($"Yedekleniyor: {d.DeviceName}");
            var legacy = ToLegacy(d);
            if (!await _drivers.ExportDriverAsync(legacy, folder, ct).ConfigureAwait(false))
                ok = false;
        }
        if (ok)
        {
            _meta.LastBackup = DateTime.Now;
            SaveMeta();
            _log.Success("Seçili sürücüler yedeklendi.", folder);
        }
        return ok;
    }

    public async Task<bool> RestoreFromBackupAsync(string infPath, IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("Geri yükleme noktası oluşturuluyor...");
        if (!await _restore.CreateAsync("Fendr - sürücü geri yükleme öncesi", ct).ConfigureAwait(false))
        {
            _log.Warning("Geri yükleme noktası oluşturulamadı — işlem iptal.");
            return false;
        }
        progress?.Report($"Sürücü yükleniyor: {Path.GetFileName(infPath)}");
        var ok = await _drivers.RestoreDriverAsync(infPath, ct).ConfigureAwait(false);
        if (ok)
        {
            _meta.LastRestore = DateTime.Now;
            SaveMeta();
            _log.Success("Sürücü geri yüklendi.", infPath);
        }
        return ok;
    }

    public Task<bool> ExportDriverAsync(DriverDevice device, string folder, CancellationToken ct = default) =>
        _drivers.ExportDriverAsync(ToLegacy(device), folder, ct);

    public Task<string> ExportReportAsync(string folder, string format, IReadOnlyList<DriverDevice> devices, CancellationToken ct = default) =>
        Task.Run(() =>
        {
            Directory.CreateDirectory(folder);
            var ts = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var path = format.ToLowerInvariant() switch
            {
                "json" => WriteJson(folder, ts, devices),
                "csv" => WriteCsv(folder, ts, devices),
                "html" => WriteHtml(folder, ts, devices),
                "txt" => WriteTxt(folder, ts, devices),
                _ => WriteCsv(folder, ts, devices)
            };
            _log.Success($"Sürücü raporu dışa aktarıldı ({format}).", path);
            return path;
        }, ct);

    public Task<IReadOnlyList<DriverStorePackage>> GetDriverStoreAsync(CancellationToken ct = default) =>
        Task.Run<IReadOnlyList<DriverStorePackage>>(async () =>
        {
            var output = await ProcessRunner.CaptureAsync("pnputil", "/enum-drivers", ct).ConfigureAwait(false);
            return ParseDriverStore(output);
        }, ct);

    public async Task<bool> CleanupUnusedDriversAsync(IProgress<string>? progress, CancellationToken ct = default)
    {
        progress?.Report("Kullanılmayan sürücü paketleri aranıyor...");
        var store = await GetDriverStoreAsync().ConfigureAwait(false);
        var unused = store.Where(p => !p.InUse).ToList();
        if (unused.Count == 0)
        {
            progress?.Report("Temizlenecek kullanılmayan paket yok.");
            return true;
        }
        var ok = true;
        foreach (var pkg in unused)
        {
            progress?.Report($"Siliniyor: {pkg.PublishedName}");
            var result = await ProcessRunner.RunAsync("pnputil",
                $"/delete-driver {pkg.PublishedName} /uninstall /force", null, CancellationToken.None).ConfigureAwait(false);
            if (result.ExitCode != 0) ok = false;
        }
        _log.Info($"Sürücü deposu temizliği: {unused.Count} paket işlendi.");
        return ok;
    }

    public async Task<bool> SetDeviceEnabledAsync(DriverDevice device, bool enabled, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(device.DeviceInstanceId)) return false;
        var action = enabled ? "Enable-PnpDevice" : "Disable-PnpDevice";
        var script = $"{action} -InstanceId '{EscapePs(device.DeviceInstanceId)}' -Confirm:$false";
        var result = await ProcessRunner.RunAsync("powershell",
            $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"", null, ct).ConfigureAwait(false);
        var ok = result.ExitCode == 0;
        if (ok) _log.Success(enabled ? "Cihaz etkinleştirildi" : "Cihaz devre dışı", device.DeviceName);
        else _log.Warning("Cihaz durumu değiştirilemedi.", result.Output);
        return ok;
    }

    public async Task RescanHardwareAsync(CancellationToken ct = default)
    {
        await ProcessRunner.RunAsync("powershell",
            "-NoProfile -Command \"Get-PnpDevice | Out-Null\"", null, ct).ConfigureAwait(false);
        _log.Info("Donanım yeniden tarandı.");
    }

    public void OpenDeviceManager() =>
        Process.Start(new ProcessStartInfo("devmgmt.msc") { UseShellExecute = true });

    public void OpenWindowsUpdateDrivers() =>
        TryOpenUri("ms-settings:windowsupdate");

    public void OpenOptionalDriverUpdates() =>
        TryOpenUri("ms-settings:windowsupdate-optionalupdates");

    public void OpenMicrosoftUpdateCatalog(string hardwareId)
    {
        var query = Uri.EscapeDataString(hardwareId.Trim());
        var url = $"https://www.catalog.update.microsoft.com/Search.aspx?q={query}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        _log.Info("Microsoft Update Catalog açıldı", hardwareId);
    }

    private static void TryOpenUri(string uri)
    {
        try { Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true }); }
        catch { Process.Start(new ProcessStartInfo("ms-settings:windowsupdate") { UseShellExecute = true }); }
    }

    // ----- WMI ----------------------------------------------------------------

    private static SignedDriver? FindSignedDriver(
        Dictionary<string, SignedDriver> index, string[] hardwareIds)
    {
        foreach (var id in hardwareIds)
        {
            var key = NormalizeHwId(id);
            if (index.TryGetValue(key, out var driver)) return driver;
        }
        return null;
    }

    private static string NormalizeHwId(string id) =>
        id.Trim().ToUpperInvariant();

    private Dictionary<string, SignedDriver> BuildSignedDriverIndex()
    {
        var index = new Dictionary<string, SignedDriver>(StringComparer.OrdinalIgnoreCase);
        foreach (var driver in QuerySignedDrivers())
        {
            foreach (var id in driver.HardwareIds)
            {
                var key = NormalizeHwId(id);
                if (!index.ContainsKey(key))
                    index[key] = driver;
            }
        }
        return index;
    }

    private List<PnpEntity> QueryPnPEntities(IProgress<string>? progress, CancellationToken ct)
    {
        var list = new List<PnpEntity>();
        try
        {
            var scope = new ManagementScope(@"\\.\root\cimv2");
            scope.Options.EnablePrivileges = true;
            scope.Connect();

            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery(
                    "SELECT Name, PNPClass, Manufacturer, DeviceID, ClassGuid, ConfigManagerErrorCode, Status, Service, HardWareID, CompatibleID, LocationInformation, Present FROM Win32_PnPEntity"));
            searcher.Options.Timeout = TimeSpan.FromMinutes(2);

            using var results = searcher.Get();
            var total = results.Count;
            var i = 0;
            foreach (var o in results)
            {
                ct.ThrowIfCancellationRequested();
                using (o)
                {
                    var problem = Int(o["ConfigManagerErrorCode"]);
                    list.Add(new PnpEntity
                    {
                        Name = StrOrEmpty(o["Name"]),
                        Class = StrOrEmpty(o["PNPClass"]),
                        Manufacturer = StrOrEmpty(o["Manufacturer"]),
                        InstanceId = StrOrEmpty(o["DeviceID"]),
                        ClassGuid = StrOrEmpty(o["ClassGuid"]),
                        ProblemCode = problem,
                        Status = StrOrEmpty(o["Status"]),
                        Service = StrOrEmpty(o["Service"]),
                        Location = StrOrEmpty(o["LocationInformation"]),
                        HardwareIds = ToArray(o["HardWareID"]),
                        CompatibleIds = ToArray(o["CompatibleID"]),
                        IsEnabled = problem != 22,
                        IsPresent = o["Present"] is bool p && p
                    });
                }
                i++;
                if (i % 50 == 0)
                    progress?.Report($"WMI: {i}/{total} cihaz okundu...");
            }
        }
        catch (Exception ex)
        {
            _log.Error("Win32_PnPEntity taraması başarısız", ex);
            list.AddRange(QueryPnPEntitiesViaCim(progress, ct));
        }
        return list;
    }

    private List<PnpEntity> QueryPnPEntitiesViaCim(IProgress<string>? progress, CancellationToken ct)
    {
        var list = new List<PnpEntity>();
        try
        {
            progress?.Report("CIM yedek taraması çalışıyor...");
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name, PNPClass, Manufacturer, DeviceID, ClassGuid, ConfigManagerErrorCode, Status, Service, HardWareID, CompatibleID FROM Win32_PnPEntity");
            foreach (var o in searcher.Get())
            {
                ct.ThrowIfCancellationRequested();
                using (o)
                {
                    var problem = Int(o["ConfigManagerErrorCode"]);
                    list.Add(new PnpEntity
                    {
                        Name = StrOrEmpty(o["Name"]),
                        Class = StrOrEmpty(o["PNPClass"]),
                        Manufacturer = StrOrEmpty(o["Manufacturer"]),
                        InstanceId = StrOrEmpty(o["DeviceID"]),
                        ClassGuid = StrOrEmpty(o["ClassGuid"]),
                        ProblemCode = problem,
                        Status = StrOrEmpty(o["Status"]),
                        Service = StrOrEmpty(o["Service"]),
                        HardwareIds = ToArray(o["HardWareID"]),
                        CompatibleIds = ToArray(o["CompatibleID"]),
                        IsEnabled = problem != 22,
                        IsPresent = true
                    });
                }
            }
        }
        catch (Exception ex) { _log.Error("CIM yedek taraması başarısız", ex); }
        return list;
    }

    private List<SignedDriver> QuerySignedDrivers()
    {
        var list = new List<SignedDriver>();
        var queries = new[]
        {
            "SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, HardWareID, InfName, IsSigned, DriverSigner FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL",
            "SELECT DeviceName, DeviceClass, Manufacturer, DriverProviderName, DriverVersion, DriverDate, HardWareID, InfName, IsSigned FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL"
        };

        foreach (var wql in queries)
        {
            try
            {
                var scope = new ManagementScope(@"\\.\root\cimv2");
                scope.Options.EnablePrivileges = true;
                scope.Connect();
                using var searcher = new ManagementObjectSearcher(scope, new ObjectQuery(wql));
                searcher.Options.Timeout = TimeSpan.FromMinutes(2);
                foreach (var o in searcher.Get())
                {
                    using (o)
                    {
                        list.Add(new SignedDriver
                        {
                            DeviceName = StrOrEmpty(o["DeviceName"]),
                            Class = StrOrEmpty(o["DeviceClass"]),
                            Manufacturer = StrOrEmpty(o["Manufacturer"]),
                            Provider = StrOrEmpty(o["DriverProviderName"]),
                            Version = StrOrEmpty(o["DriverVersion"]),
                            Date = ParseWmiDate(o["DriverDate"]),
                            InfName = StrOrEmpty(o["InfName"]),
                            IsSigned = o["IsSigned"] is bool b && b,
                            Signer = StrOrEmpty(o["DriverSigner"]),
                            HardwareIds = ToArray(o["HardWareID"])
                        });
                    }
                }
                if (list.Count > 0) break;
            }
            catch (Exception ex) { _log.Warning("Win32_PnPSignedDriver sorgusu", ex.Message); }
        }
        return list;
    }

    private static DriverDevice MergeDevice(PnpEntity e, SignedDriver? s)
    {
        var hwIds = e.HardwareIds.Length > 0 ? e.HardwareIds : s?.HardwareIds ?? Array.Empty<string>();
        var cls = !string.IsNullOrWhiteSpace(e.Class) && !e.Class.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? e.Class : s?.Class ?? "Bilinmiyor";
        var problem = e.ProblemCode;
        var isMissing = problem is 1 or 10 or 18 or 28 or 31;

        return new DriverDevice
        {
            DeviceName = !string.IsNullOrWhiteSpace(e.Name) ? e.Name : s?.DeviceName ?? "Bilinmeyen Cihaz",
            DeviceClass = cls,
            Category = MapCategory(cls),
            Manufacturer = !string.IsNullOrWhiteSpace(e.Manufacturer) ? e.Manufacturer : s?.Manufacturer ?? "Bilinmiyor",
            Provider = !string.IsNullOrWhiteSpace(s?.Provider) ? s.Provider : "Bilinmiyor",
            DriverVersion = !string.IsNullOrWhiteSpace(s?.Version) ? s.Version : "Yüklü değil",
            DriverDate = s?.Date,
            HardwareId = hwIds.FirstOrDefault() ?? string.Empty,
            HardwareIds = hwIds,
            CompatibleIds = e.CompatibleIds,
            DeviceInstanceId = e.InstanceId,
            ClassGuid = e.ClassGuid,
            InfName = s?.InfName ?? string.Empty,
            InfPath = !string.IsNullOrEmpty(s?.InfName) ? $@"C:\Windows\INF\{s.InfName}" : string.Empty,
            DriverStorePath = @"C:\Windows\System32\DriverStore\FileRepository",
            Service = e.Service,
            Location = e.Location,
            Signer = !string.IsNullOrWhiteSpace(s?.Signer) ? s.Signer : s?.IsSigned == true ? "İmzalı" : "Bilinmiyor",
            IsSigned = s?.IsSigned ?? false,
            Status = string.IsNullOrWhiteSpace(e.Status) ? (e.IsPresent ? "OK" : "Bağlı değil") : e.Status,
            DeviceState = e.IsEnabled ? "Etkin" : "Devre Dışı",
            ProblemCode = problem,
            IsEnabled = e.IsEnabled,
            IsMissing = isMissing
        };
    }

    private DriverCenterStats BuildStats(IReadOnlyList<DriverDevice> devices)
    {
        var findings = new List<string>();
        var score = 100;

        var missing = devices.Count(d => d.IsMissing);
        var unsigned = devices.Count(d => !d.IsSigned && !d.IsUnknown);
        var disabled = devices.Count(d => !d.IsEnabled);
        var unknown = devices.Count(d => d.IsUnknown);
        var problems = devices.Count(d => d.ProblemCode != 0);
        var outdated = devices.Count(d => d.IsOutdated);

        if (missing > 0) { score -= Math.Min(missing * 8, 30); findings.Add($"{missing} eksik sürücü (-{Math.Min(missing * 8, 30)})"); }
        if (unsigned > 0) { score -= Math.Min(unsigned * 3, 15); findings.Add($"{unsigned} imzasız sürücü (-{Math.Min(unsigned * 3, 15)})"); }
        if (problems > 0) { score -= Math.Min(problems * 5, 25); findings.Add($"{problems} sorunlu cihaz (-{Math.Min(problems * 5, 25)})"); }
        if (disabled > 3) { score -= 5; findings.Add($"{disabled} devre dışı cihaz (-5)"); }
        if (unknown > 0) { score -= Math.Min(unknown * 2, 10); findings.Add($"{unknown} bilinmeyen cihaz (-{Math.Min(unknown * 2, 10)})"); }

        var old = devices.Count(d => d.DriverDate is { } dt && dt < DateTime.Now.AddYears(-5));
        if (old > 0) { score -= Math.Min(old, 10); findings.Add($"{old} eski sürücü (-{Math.Min(old, 10)})"); }

        score = Math.Clamp(score, 0, 100);
        if (findings.Count == 0) findings.Add("Sorun tespit edilmedi.");

        return new DriverCenterStats
        {
            InstalledCount = devices.Count(d => !d.IsMissing),
            MissingCount = missing,
            OutdatedCount = outdated,
            UnsignedCount = unsigned,
            DisabledCount = disabled,
            UnknownCount = unknown,
            ProblemCount = problems,
            HealthScore = score,
            HealthRating = score switch { >= 90 => "Mükemmel", >= 75 => "İyi", >= 50 => "Orta", _ => "Dikkat" },
            LastScan = _meta.LastScan,
            LastBackup = _meta.LastBackup,
            LastRestore = _meta.LastRestore,
            BackupStatus = _meta.LastBackup is { } b ? $"Son yedek: {b:dd.MM.yyyy HH:mm}" : "Yedek yok",
            HealthFindings = findings
        };
    }

    private static string MapCategory(string cls) => cls.ToUpperInvariant() switch
    {
        "DISPLAY" => "Ekran Kartları",
        "PROCESSOR" => "İşlemciler",
        "NET" or "NETSERVICE" => "Ağ Bağdaştırıcıları",
        "BLUETOOTH" => "Bluetooth",
        "MEDIA" or "AUDIOENDPOINT" or "MEDIA_CLASS" => "Ses Cihazları",
        "DISKDRIVE" or "HDC" => "Depolama",
        "USB" => "USB",
        "KEYBOARD" or "MOUSE" or "HIDCLASS" => "Giriş Cihazları",
        "PRINTER" => "Yazıcılar",
        "MONITOR" => "Monitörler",
        "SYSTEM" or "COMPUTER" or "BASEBOARD" => "Anakart / Sistem",
        "BIOMETRIC" or "SECURITYDEVICES" => "Güvenlik",
        "WPD" or "PORTABLE" => "Taşınabilir",
        _ when cls.Contains("SCSI", StringComparison.OrdinalIgnoreCase) => "Depolama Denetleyicileri",
        _ => string.IsNullOrWhiteSpace(cls) || cls == "Bilinmiyor" ? "Bilinmeyen" : cls
    };

    private static List<DriverStorePackage> ParseDriverStore(string output)
    {
        var list = new List<DriverStorePackage>();
        DriverStorePackage? current = null;
        foreach (var line in output.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Published Name", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null) list.Add(current);
                current = new DriverStorePackage { PublishedName = t.Split(':', 2).Last().Trim() };
            }
            else if (current is not null)
            {
                if (t.StartsWith("Original Name", StringComparison.OrdinalIgnoreCase))
                    current.OriginalName = t.Split(':', 2).Last().Trim();
                else if (t.StartsWith("Class Name", StringComparison.OrdinalIgnoreCase))
                    current.ClassName = t.Split(':', 2).Last().Trim();
                else if (t.StartsWith("Provider Name", StringComparison.OrdinalIgnoreCase))
                    current.Provider = t.Split(':', 2).Last().Trim();
                else if (t.StartsWith("Driver Version", StringComparison.OrdinalIgnoreCase))
                    current.Version = t.Split(':', 2).Last().Trim();
            }
        }
        if (current is not null) list.Add(current);
        return list;
    }

    private static DriverInfo ToLegacy(DriverDevice d) => new()
    {
        DeviceName = d.DeviceName,
        DeviceClass = d.DeviceClass,
        Manufacturer = d.Manufacturer,
        Provider = d.Provider,
        CurrentVersion = d.DriverVersion,
        DriverDate = d.DriverDate,
        HardwareId = d.HardwareId,
        InfName = d.InfName,
        IsSigned = d.IsSigned,
        IsMissing = d.IsMissing,
        IsOutdated = d.IsOutdated,
        LatestVersion = d.LatestVersion
    };

    private static string WriteCsv(string folder, string ts, IReadOnlyList<DriverDevice> devices)
    {
        var path = Path.Combine(folder, $"drivers-{ts}.csv");
        var sb = new StringBuilder();
        sb.AppendLine("Cihaz,Sınıf,Üretici,Sağlayıcı,Sürüm,Tarih,İmza,Durum,Sorun");
        foreach (var d in devices)
            sb.AppendLine($"\"{d.DeviceName}\",\"{d.DeviceClass}\",\"{d.Manufacturer}\",\"{d.Provider}\",\"{d.DriverVersion}\",\"{d.DriverDate:yyyy-MM-dd}\",\"{d.SignatureStatus}\",\"{d.DeviceState}\",\"{d.ProblemText}\"");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string WriteJson(string folder, string ts, IReadOnlyList<DriverDevice> devices)
    {
        var path = Path.Combine(folder, $"drivers-{ts}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(devices, new JsonSerializerOptions { WriteIndented = true }));
        return path;
    }

    private static string WriteTxt(string folder, string ts, IReadOnlyList<DriverDevice> devices)
    {
        var path = Path.Combine(folder, $"drivers-{ts}.txt");
        var sb = new StringBuilder();
        foreach (var d in devices)
            sb.AppendLine($"{d.DeviceName} | {d.DeviceClass} | {d.DriverVersion} | {d.SignatureStatus}");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private static string WriteHtml(string folder, string ts, IReadOnlyList<DriverDevice> devices)
    {
        var path = Path.Combine(folder, $"drivers-{ts}.html");
        var sb = new StringBuilder();
        sb.Append("<html><head><meta charset='utf-8'><title>Fendr Sürücü Raporu</title></head><body>");
        sb.Append("<h1>Fendr System Care — Sürücü Raporu</h1><table border='1' cellpadding='6'>");
        sb.Append("<tr><th>Cihaz</th><th>Sınıf</th><th>Sürüm</th><th>İmza</th><th>Durum</th></tr>");
        foreach (var d in devices)
            sb.Append($"<tr><td>{d.DeviceName}</td><td>{d.DeviceClass}</td><td>{d.DriverVersion}</td><td>{d.SignatureStatus}</td><td>{d.DeviceState}</td></tr>");
        sb.Append("</table></body></html>");
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        return path;
    }

    private void LoadMeta()
    {
        try
        {
            if (!File.Exists(_metaPath)) return;
            _meta = JsonSerializer.Deserialize<DriverCenterMeta>(File.ReadAllText(_metaPath)) ?? new();
            if (_meta.BackupHistory is not null)
                _backupHistory.AddRange(_meta.BackupHistory);
        }
        catch { }
    }

    private void SaveMeta()
    {
        _meta.BackupHistory = _backupHistory.Take(20).ToList();
        try { File.WriteAllText(_metaPath, JsonSerializer.Serialize(_meta, new JsonSerializerOptions { WriteIndented = true })); }
        catch { }
    }

    private static string Str(object? v) => v?.ToString()?.Trim() ?? "Bilinmiyor";
    private static string StrOrEmpty(object? v) => v?.ToString()?.Trim() ?? string.Empty;
    private static int Int(object? v) { try { return v is null ? 0 : Convert.ToInt32(v); } catch { return 0; } }
    private static string[] ToArray(object? v) => v switch
    {
        string[] a => a,
        string s when !string.IsNullOrEmpty(s) => new[] { s },
        _ => Array.Empty<string>()
    };
    private static DateTime? ParseWmiDate(object? v)
    {
        var raw = v?.ToString();
        if (string.IsNullOrEmpty(raw) || raw.Length < 8) return null;
        return DateTime.TryParseExact(raw[..8], "yyyyMMdd", null, System.Globalization.DateTimeStyles.None, out var d) ? d : null;
    }
    private static string EscapePs(string s) => s.Replace("'", "''");

    private sealed class DriverCenterMeta
    {
        public DateTime? LastScan { get; set; }
        public DateTime? LastBackup { get; set; }
        public DateTime? LastRestore { get; set; }
        public string? BackupPath { get; set; }
        public List<DriverBackupRecord>? BackupHistory { get; set; }
    }

    private sealed class PnpEntity
    {
        public string Name { get; init; } = string.Empty;
        public string Class { get; init; } = string.Empty;
        public string Manufacturer { get; init; } = string.Empty;
        public string InstanceId { get; init; } = string.Empty;
        public string ClassGuid { get; init; } = string.Empty;
        public int ProblemCode { get; init; }
        public string Status { get; init; } = string.Empty;
        public string Service { get; init; } = string.Empty;
        public string Location { get; init; } = string.Empty;
        public string[] HardwareIds { get; init; } = Array.Empty<string>();
        public string[] CompatibleIds { get; init; } = Array.Empty<string>();
        public bool IsEnabled { get; init; } = true;
        public bool IsPresent { get; init; } = true;
    }

    private sealed class SignedDriver
    {
        public string DeviceName { get; init; } = string.Empty;
        public string Class { get; init; } = string.Empty;
        public string Manufacturer { get; init; } = string.Empty;
        public string Provider { get; init; } = string.Empty;
        public string Version { get; init; } = string.Empty;
        public DateTime? Date { get; init; }
        public string InfName { get; init; } = string.Empty;
        public bool IsSigned { get; init; }
        public string Signer { get; init; } = string.Empty;
        public string[] HardwareIds { get; init; } = Array.Empty<string>();
    }
}
