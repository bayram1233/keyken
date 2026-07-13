using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;
using LibreHardwareMonitor.Hardware;
using Microsoft.Win32;
using SysInfo = FendrSystemCare.Models.SystemInformation;

namespace FendrSystemCare.Services;

/// <summary>
/// Dashboard kontrol paneli servisi. WMI, Performance Counter, LibreHardwareMonitor
/// ve Windows API'lerinden gerçek sistem verilerini toplar. Statik veri 30 sn
/// önbelleklenir; canlı metrikler saniyede bir güncellenir.
/// </summary>
public sealed class DashboardService : IDashboardService, IDisposable
{
    private readonly ISystemInfoService _systemInfo;
    private readonly IMonitoringService _monitoring;
    private readonly IDriverService _drivers;
    private readonly ILoggingService _log;
    private readonly IWindowsUpdateService _updates;

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private DashboardSnapshot? _cachedSnapshot;
    private DateTime _cacheTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);

    private Computer? _lhm;
    private bool _lhmReady;
    private bool _livePaused;

    public PerformanceSample LatestSample => _monitoring.Latest;
    public event EventHandler<PerformanceSample>? LiveSampleAvailable;

    public DashboardService(ISystemInfoService systemInfo, IMonitoringService monitoring,
        IDriverService drivers, ILoggingService log, IWindowsUpdateService updates)
    {
        _systemInfo = systemInfo;
        _monitoring = monitoring;
        _drivers = drivers;
        _log = log;
        _updates = updates;
        _monitoring.SampleAvailable += OnMonitoringSample;
    }

    public void StartLiveMonitoring()
    {
        TryInitLhm();
        _monitoring.Start();
    }

    public void StopLiveMonitoring() => _monitoring.Stop();

    public void SetLivePaused(bool paused) => _livePaused = paused;

    public async Task<DashboardSnapshot> LoadSnapshotAsync(CancellationToken ct = default)
    {
        if (_cachedSnapshot is not null && DateTime.Now - _cacheTime < CacheDuration)
            return _cachedSnapshot;

        var sw = Stopwatch.StartNew();
        using var scope = _log.BeginOperation("Dashboard veri yükleme");

        try
        {
            var health = await _systemInfo.ComputeHealthScoreAsync(ct).ConfigureAwait(false);
            var baseInfo = await _systemInfo.GetSystemInformationAsync(ct).ConfigureAwait(false);
            var uptime = await _systemInfo.GetUptimeAsync(ct).ConfigureAwait(false);
            var volumes = await _systemInfo.GetStorageVolumesAsync(ct).ConfigureAwait(false);

            var system = QuerySystemInfo(baseInfo, uptime);
            var cpu = QueryCpuInfo(baseInfo);
            var gpu = QueryGpuInfo(baseInfo);
            var memory = QueryMemoryInfo(baseInfo);
            var network = await QueryNetworkInfoAsync(ct).ConfigureAwait(false);
            var drives = QueryDrives(volumes, baseInfo.Disks);
            var activities = BuildActivities();
            var performance = ComputePerformanceScores(health, baseInfo, volumes, cpu, memory, gpu, system);
            var alerts = BuildAlerts(health, baseInfo, volumes, cpu, gpu, memory, system);
            var summary = BuildSummaryCards(health, baseInfo, volumes, cpu, gpu, memory, network, system, alerts);

            var snapshot = new DashboardSnapshot
            {
                Health = TranslateHealth(health),
                Performance = performance,
                System = system,
                Cpu = cpu,
                Gpu = gpu,
                Memory = memory,
                Network = network,
                Drives = drives,
                SummaryCards = summary,
                Alerts = alerts,
                Activities = activities,
                LoadedAt = DateTime.Now
            };

            _cachedSnapshot = snapshot;
            _cacheTime = DateTime.Now;
            scope.Complete($"{sw.ElapsedMilliseconds} ms");
            return snapshot;
        }
        catch (Exception ex)
        {
            scope.Fail(ex.Message, ex);
            throw;
        }
    }

    public async Task<DashboardLayoutSettings> LoadLayoutAsync(CancellationToken ct = default)
    {
        var path = LayoutPath();
        if (!File.Exists(path)) return new DashboardLayoutSettings();
        try
        {
            var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize<DashboardLayoutSettings>(json) ?? new DashboardLayoutSettings();
        }
        catch { return new DashboardLayoutSettings(); }
    }

    public async Task SaveLayoutAsync(DashboardLayoutSettings layout, CancellationToken ct = default)
    {
        var path = LayoutPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var json = JsonSerializer.Serialize(layout, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
    }

    public Task ResetLayoutAsync(CancellationToken ct = default)
    {
        var path = LayoutPath();
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private void OnMonitoringSample(object? sender, PerformanceSample sample)
    {
        if (!_livePaused)
            LiveSampleAvailable?.Invoke(this, sample);
    }

    private void TryInitLhm()
    {
        if (_lhmReady) return;
        try
        {
            _lhm = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsMemoryEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true
            };
            _lhm.Open();
            _lhmReady = true;
        }
        catch (Exception ex)
        {
            _log.Warning("LibreHardwareMonitor başlatılamadı.", ex.Message);
        }
    }

    private (double? cpuTemp, double? cpuPower, double? cpuVoltage, double? gpuTemp, double? gpuPower) ReadLhmSensors()
    {
        if (!_lhmReady || _lhm is null) return (null, null, null, null, null);

        double? cpuTemp = null, cpuPower = null, cpuVoltage = null, gpuTemp = null, gpuPower = null;
        try
        {
            foreach (var hw in _lhm.Hardware)
            {
                hw.Update();
                foreach (var sensor in hw.Sensors)
                {
                    if (sensor.Value is null) continue;
                    if (hw.HardwareType == HardwareType.Cpu)
                    {
                        if (sensor.SensorType == SensorType.Temperature && (cpuTemp is null || sensor.Value > cpuTemp))
                            cpuTemp = sensor.Value;
                        if (sensor.SensorType == SensorType.Power && sensor.Name.Contains("Package", StringComparison.OrdinalIgnoreCase))
                            cpuPower = sensor.Value;
                        if (sensor.SensorType == SensorType.Voltage && sensor.Name.Contains("Core", StringComparison.OrdinalIgnoreCase))
                            cpuVoltage = sensor.Value;
                    }
                    if (hw.HardwareType is HardwareType.GpuNvidia or HardwareType.GpuAmd or HardwareType.GpuIntel)
                    {
                        if (sensor.SensorType == SensorType.Temperature && (gpuTemp is null || sensor.Value > gpuTemp))
                            gpuTemp = sensor.Value;
                        if (sensor.SensorType == SensorType.Power && (gpuPower is null || sensor.Value > gpuPower))
                            gpuPower = sensor.Value;
                    }
                }
            }
        }
        catch { /* Sensör okuma isteğe bağlı. */ }
        return (cpuTemp, cpuPower, cpuVoltage, gpuTemp, gpuPower);
    }

    // ----- Sorgular ---------------------------------------------------------

    private DashboardSystemInfo QuerySystemInfo(SysInfo info, TimeSpan uptime)
    {
        var installDate = "Bilinmiyor";
        var bootMode = "Bilinmiyor";
        try
        {
            using var os = new ManagementObjectSearcher("SELECT InstallDate FROM Win32_OperatingSystem");
            foreach (var o in os.Get())
            {
                var raw = o["InstallDate"]?.ToString() ?? "";
                if (raw.Length >= 8)
                    installDate = $"{raw[..4]}-{raw[4..6]}-{raw[6..8]}";
            }
        }
        catch { }

        try
        {
            using var cs = new ManagementObjectSearcher("SELECT BootupState FROM Win32_ComputerSystem");
            foreach (var o in cs.Get()) bootMode = Str(o["BootupState"]);
        }
        catch { }

        var firmware = QueryFirmwareType();
        if (firmware == 2) bootMode = "UEFI";
        else if (firmware == 1) bootMode = "Legacy BIOS";

        return new DashboardSystemInfo
        {
            ComputerName = Environment.MachineName,
            CurrentUser = Environment.UserName,
            WindowsEdition = info.WindowsEdition,
            WindowsBuild = info.WindowsBuild,
            WindowsVersion = info.WindowsVersion,
            InstallDate = installDate,
            Uptime = FormatHelper.Uptime(uptime),
            Architecture = Environment.Is64BitOperatingSystem ? "x64" : "x86",
            Language = CultureInfo.CurrentUICulture.DisplayName,
            Timezone = TimeZoneInfo.Local.DisplayName,
            BootMode = bootMode,
            SecureBoot = QuerySecureBoot(),
            TpmVersion = QueryTpmVersion(),
            BitLockerStatus = QueryBitLocker(),
            ActivationStatus = info.ActivationStatus
        };
    }

    private DashboardCpuInfo QueryCpuInfo(SysInfo info)
    {
        var manufacturer = "Bilinmiyor";
        var socket = "Bilinmiyor";
        var arch = "Bilinmiyor";
        var currentMhz = 0.0;
        var l2 = 0;
        var l3 = 0;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Manufacturer, SocketDesignation, Architecture, CurrentClockSpeed, L2CacheSize, L3CacheSize FROM Win32_Processor");
            foreach (var o in searcher.Get())
            {
                manufacturer = Str(o["Manufacturer"]);
                socket = Str(o["SocketDesignation"]);
                arch = MapArchitecture(Int(o["Architecture"]));
                currentMhz = Int(o["CurrentClockSpeed"]);
                l2 = Int(o["L2CacheSize"]);
                l3 = Int(o["L3CacheSize"]);
            }
        }
        catch { }

        var (cpuTemp, cpuPower, cpuVoltage, _, _) = ReadLhmSensors();
        var live = _monitoring.Latest;

        return new DashboardCpuInfo
        {
            Name = info.CpuName,
            Manufacturer = manufacturer,
            Socket = socket,
            Architecture = arch,
            Cores = info.CpuCores,
            Threads = info.CpuLogicalProcessors,
            BaseClockGhz = info.CpuMaxClockGhz,
            CurrentClockGhz = Math.Round(currentMhz / 1000.0, 2),
            MaxClockGhz = info.CpuMaxClockGhz,
            UsagePercent = live.CpuPercent,
            Temperature = cpuTemp is { } t ? $"{t:0.#} °C" : live.TemperatureCelsius is { } w ? $"{w:0.#} °C" : "Yok",
            Voltage = cpuVoltage is { } v ? $"{v:0.##} V" : "Yok",
            Power = cpuPower is { } p ? $"{p:0.#} W" : "Yok",
            Cache = l3 > 0 ? $"L2 {l2} KB · L3 {l3} KB" : l2 > 0 ? $"L2 {l2} KB" : "Bilinmiyor"
        };
    }

    private DashboardGpuInfo QueryGpuInfo(SysInfo info)
    {
        var vendor = "Bilinmiyor";
        var driverDate = "Bilinmiyor";
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT AdapterCompatibility, DriverDate FROM Win32_VideoController");
            foreach (var o in searcher.Get())
            {
                vendor = Str(o["AdapterCompatibility"]);
                driverDate = ParseWmiDate(o["DriverDate"]);
            }
        }
        catch { }

        var (_, _, _, gpuTemp, gpuPower) = ReadLhmSensors();
        var live = _monitoring.Latest;

        return new DashboardGpuInfo
        {
            Name = info.GpuName,
            Vendor = vendor,
            DriverVersion = info.GpuDriverVersion,
            DriverDate = driverDate,
            UsagePercent = live.GpuPercent,
            VramUsage = info.GpuMemoryGb > 0 ? $"{info.GpuMemoryGb:0.#} GB" : "-",
            Temperature = gpuTemp is { } t ? $"{t:0.#} °C" : "Yok",
            ClockSpeed = "Yok",
            Power = gpuPower is { } p ? $"{p:0.#} W" : "Yok"
        };
    }

    private DashboardMemoryInfo QueryMemoryInfo(SysInfo info)
    {
        var slots = 0;
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT DeviceLocator FROM Win32_PhysicalMemory");
            slots = searcher.Get().Count;
        }
        catch { }

        var live = _monitoring.Latest;
        var usedGb = live.RamUsedGb;
        var availableGb = Math.Max(info.TotalRamGb - usedGb, 0);

        return new DashboardMemoryInfo
        {
            InstalledGb = info.TotalRamGb,
            AvailableGb = Math.Round(availableGb, 1),
            UsedGb = Math.Round(usedGb, 1),
            SpeedMhz = info.RamSpeedMhz,
            SlotCount = slots,
            MemoryType = info.RamType,
            UsagePercent = live.RamPercent
        };
    }

    private async Task<DashboardNetworkInfo> QueryNetworkInfoAsync(CancellationToken ct)
    {
        var adapterName = "Bilinmiyor";
        var localIp = "-";
        var gateway = "-";
        var mac = "-";
        var dns = "-";

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                    or NetworkInterfaceType.Tunnel) continue;

                adapterName = nic.Name;
                mac = string.Join(":", nic.GetPhysicalAddress().GetAddressBytes().Select(b => b.ToString("X2")));
                var ipProps = nic.GetIPProperties();
                localIp = ipProps.UnicastAddresses
                    .FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ?.Address.ToString() ?? "-";
                gateway = ipProps.GatewayAddresses.FirstOrDefault()?.Address.ToString() ?? "-";
                dns = string.Join(", ", ipProps.DnsAddresses.Select(a => a.ToString()));
                break;
            }
        }
        catch { }

        var publicIp = "-";
        var internetStatus = "Çevrimdışı";
        try
        {
            publicIp = await Http.GetStringAsync("https://api.ipify.org", ct).ConfigureAwait(false);
            internetStatus = "Bağlı";
        }
        catch { }

        var live = _monitoring.Latest;
        var mbps = live.NetworkMbps;

        return new DashboardNetworkInfo
        {
            InternetStatus = internetStatus,
            AdapterName = adapterName,
            PublicIp = publicIp.Trim(),
            LocalIp = localIp,
            Gateway = gateway,
            Dns = dns,
            MacAddress = mac,
            DownloadSpeed = mbps > 0 ? $"{mbps:0.##} Mbps" : "-",
            UploadSpeed = "-",
            CurrentUsage = mbps > 0 ? $"{mbps:0.##} Mbps" : "0 Mbps"
        };
    }

    private static IReadOnlyList<DashboardDriveInfo> QueryDrives(
        IReadOnlyList<StorageVolume> volumes, IReadOnlyList<DiskHealth> disks)
    {
        var result = new List<DashboardDriveInfo>();
        var serials = QueryDiskSerials();

        foreach (var v in volumes)
        {
            var drive = DriveInfo.GetDrives().FirstOrDefault(d =>
                d.Name.TrimEnd('\\').Equals(v.Drive, StringComparison.OrdinalIgnoreCase));

            var disk = disks.FirstOrDefault();
            result.Add(new DashboardDriveInfo
            {
                DriveLetter = v.Drive,
                Label = v.Label,
                Model = disk?.Model ?? "Bilinmiyor",
                SerialNumber = serials.GetValueOrDefault(v.Drive, "Bilinmiyor"),
                FileSystem = drive?.DriveFormat ?? "Bilinmiyor",
                CapacityGb = v.TotalGb,
                UsedGb = v.UsedGb,
                FreeGb = v.FreeGb,
                Health = disk?.HealthStatus ?? "Bilinmiyor",
                SmartStatus = disk?.HealthStatus ?? "Bilinmiyor",
                Temperature = disk?.TemperatureCelsius is { } t ? $"{t} °C" : "Yok",
                MediaType = disk?.MediaType ?? "Bilinmiyor"
            });
        }
        return result;
    }

    private static Dictionary<string, string> QueryDiskSerials()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT SerialNumber, DeviceID FROM Win32_DiskDrive");
            foreach (var o in searcher.Get())
                map[Str(o["DeviceID"])] = Str(o["SerialNumber"]);
        }
        catch { }
        return map;
    }

    // ----- Skorlar, uyarılar, özet ------------------------------------------

    private static PerformanceBreakdown ComputePerformanceScores(
        HealthScore health, SysInfo info, IReadOnlyList<StorageVolume> volumes,
        DashboardCpuInfo cpu, DashboardMemoryInfo memory, DashboardGpuInfo gpu, DashboardSystemInfo system)
    {
        var explanations = new List<string>();

        var cpuScore = Math.Clamp(100 - (int)cpu.UsagePercent / 2 + cpu.Cores * 2, 40, 100);
        explanations.Add($"İşlemci: {cpu.Cores} çekirdek, %{cpu.UsagePercent:0} kullanım → {cpuScore}/100");

        var memScore = memory.UsagePercent < 80
            ? (int)(100 - memory.UsagePercent * 0.5)
            : (int)(100 - memory.UsagePercent);
        memScore = Math.Clamp(memScore, 20, 100);
        explanations.Add($"Bellek: %{memory.UsagePercent:0} kullanım → {memScore}/100");

        var storageScore = 100;
        foreach (var v in volumes)
        {
            if (v.TotalGb > 0 && v.FreeGb / v.TotalGb < 0.10)
            {
                storageScore -= 25;
                explanations.Add($"Depolama {v.Drive}: disk %{v.UsedPercent:0} dolu (-25)");
            }
        }
        storageScore = Math.Clamp(storageScore, 10, 100);

        var gpuScore = gpu.Name != "Bilinmiyor" && gpu.Name != "Unknown"
            ? Math.Clamp(100 - (int)gpu.UsagePercent / 3, 50, 100) : 60;
        explanations.Add($"Grafik: {gpu.Name} → {gpuScore}/100");

        var winScore = info.IsActivated ? 90 : 60;
        if (system.SecureBoot == "Açık") winScore += 5;
        winScore = Math.Clamp(winScore, 30, 100);
        explanations.Add($"Windows: {system.ActivationStatus}, Güvenli Önyükleme {system.SecureBoot} → {winScore}/100");

        var overall = (cpuScore + memScore + storageScore + gpuScore + winScore) / 5;

        return new PerformanceBreakdown
        {
            CpuScore = cpuScore,
            MemoryScore = memScore,
            StorageScore = storageScore,
            GraphicsScore = gpuScore,
            WindowsScore = winScore,
            OverallScore = overall,
            Explanations = explanations
        };
    }

    private static IReadOnlyList<DashboardAlert> BuildAlerts(
        HealthScore health, SysInfo info, IReadOnlyList<StorageVolume> volumes,
        DashboardCpuInfo cpu, DashboardGpuInfo gpu, DashboardMemoryInfo memory,
        DashboardSystemInfo system)
    {
        var alerts = new List<DashboardAlert>();

        if (cpu.Temperature != "Yok" && double.TryParse(cpu.Temperature.Replace(" °C", ""), out var ct) && ct > 85)
            alerts.Add(new DashboardAlert
            {
                Title = "Yüksek CPU Sıcaklığı",
                Message = $"İşlemci sıcaklığı {ct:0}°C — soğutmayı kontrol edin.",
                Severity = DashboardHealthLevel.Critical
            });

        foreach (var v in volumes)
        {
            if (v.TotalGb > 0 && v.FreeGb / v.TotalGb < 0.10)
                alerts.Add(new DashboardAlert
                {
                    Title = "Düşük Disk Alanı",
                    Message = $"{v.Drive} sürücüsünde yalnızca {v.FreeGb:0} GB boş alan kaldı.",
                    Severity = DashboardHealthLevel.Warning
                });
        }

        if (memory.UsagePercent > 90)
            alerts.Add(new DashboardAlert
            {
                Title = "Bellek Baskısı",
                Message = $"RAM kullanımı %{memory.UsagePercent:0} — bellek temizliği düşünün.",
                Severity = DashboardHealthLevel.Warning
            });

        if (!info.IsActivated)
            alerts.Add(new DashboardAlert
            {
                Title = "Windows Etkinleştirilmemiş",
                Message = "Windows lisansı etkin değil.",
                Severity = DashboardHealthLevel.Warning
            });

        if (system.SecureBoot == "Kapalı")
            alerts.Add(new DashboardAlert
            {
                Title = "Güvenli Önyükleme Kapalı",
                Message = "Güvenli Önyükleme devre dışı — güvenlik riski.",
                Severity = DashboardHealthLevel.Warning
            });

        foreach (var finding in health.Findings.Where(f => !f.Contains("No issues") && !f.Contains("Sorun tespit")))
        {
            alerts.Add(new DashboardAlert
            {
                Title = "Sistem Sağlığı",
                Message = finding,
                Severity = DashboardHealthLevel.Warning
            });
        }

        if (alerts.Count == 0)
            alerts.Add(new DashboardAlert
            {
                Title = "Her Şey Yolunda",
                Message = "Kritik uyarı bulunamadı.",
                Severity = DashboardHealthLevel.Excellent
            });

        return alerts;
    }

    private static IReadOnlyList<DashboardSummaryCard> BuildSummaryCards(
        HealthScore health, SysInfo info, IReadOnlyList<StorageVolume> volumes,
        DashboardCpuInfo cpu, DashboardGpuInfo gpu, DashboardMemoryInfo memory,
        DashboardNetworkInfo network, DashboardSystemInfo system,
        IReadOnlyList<DashboardAlert> alerts)
    {
        var diskHealth = info.Disks.FirstOrDefault()?.HealthStatus ?? "Bilinmiyor";
        var diskLevel = diskHealth is "Healthy" or "OK" or "Sağlıklı"
            ? DashboardHealthLevel.Excellent : DashboardHealthLevel.Warning;

        return
        [
            new DashboardSummaryCard
            {
                Key = "health", Title = "Sistem Sağlığı", Description = "Genel durum skoru",
                Icon = "ShieldCheckmark24", StatusText = $"{health.Score}/100 · {health.Rating}",
                Progress = health.Score, Level = ScoreToLevel(health.Score), ActionLabel = "Detay"
            },
            new DashboardSummaryCard
            {
                Key = "windows", Title = "Windows", Description = system.WindowsEdition,
                Icon = "Window24", StatusText = system.ActivationStatus,
                Progress = info.IsActivated ? 100 : 50,
                Level = info.IsActivated ? DashboardHealthLevel.Excellent : DashboardHealthLevel.Warning,
                ActionLabel = "Güncelle"
            },
            new DashboardSummaryCard
            {
                Key = "drivers", Title = "Sürücüler", Description = "Donanım sürücü durumu",
                Icon = "DeveloperBoard24",
                StatusText = alerts.Any(a => a.Message.Contains("driver") || a.Message.Contains("sürücü"))
                    ? "Sorun var" : "Güncel",
                Progress = alerts.Any(a => a.Message.Contains("driver")) ? 60 : 95,
                Level = alerts.Any(a => a.Message.Contains("driver")) ? DashboardHealthLevel.Warning : DashboardHealthLevel.Good,
                ActionLabel = "Tara"
            },
            new DashboardSummaryCard
            {
                Key = "security", Title = "Güvenlik", Description = "Güvenli Önyükleme & TPM",
                Icon = "LockClosed24",
                StatusText = $"GÖ: {system.SecureBoot} · TPM: {system.TpmVersion}",
                Progress = system.SecureBoot == "Açık" ? 95 : 55,
                Level = system.SecureBoot == "Açık" ? DashboardHealthLevel.Excellent : DashboardHealthLevel.Warning,
                ActionLabel = "İncele"
            },
            new DashboardSummaryCard
            {
                Key = "disk", Title = "Disk Sağlığı", Description = diskHealth,
                Icon = "HardDrive24", StatusText = diskHealth,
                Progress = diskLevel == DashboardHealthLevel.Excellent ? 95 : 50,
                Level = diskLevel, ActionLabel = "SMART"
            },
            new DashboardSummaryCard
            {
                Key = "memory", Title = "Bellek", Description = $"{memory.InstalledGb} GB {memory.MemoryType}",
                Icon = "Storage24", StatusText = $"%{memory.UsagePercent:0} kullanım",
                Progress = 100 - memory.UsagePercent,
                Level = memory.UsagePercent > 85 ? DashboardHealthLevel.Warning : DashboardHealthLevel.Good,
                ActionLabel = "Temizle"
            },
            new DashboardSummaryCard
            {
                Key = "temperature", Title = "Sıcaklık", Description = "İşlemci & GPU",
                Icon = "WeatherSunny24", StatusText = cpu.Temperature,
                Progress = ParseTempProgress(cpu.Temperature),
                Level = ParseTempLevel(cpu.Temperature), ActionLabel = "İzle"
            },
            new DashboardSummaryCard
            {
                Key = "network", Title = "Ağ", Description = network.AdapterName,
                Icon = "Wifi124", StatusText = network.InternetStatus,
                Progress = network.InternetStatus == "Bağlı" ? 100 : 0,
                Level = network.InternetStatus == "Bağlı" ? DashboardHealthLevel.Excellent : DashboardHealthLevel.Critical,
                ActionLabel = "Test"
            }
        ];
    }

    private IReadOnlyList<DashboardActivity> BuildActivities()
    {
        return _log.Entries
            .Take(15)
            .Select(e => new DashboardActivity
            {
                Time = e.Timestamp,
                Operation = e.Message,
                Status = e.Level.ToString(),
                Result = e.Detail ?? (e.Level == LogLevel.Success ? "Başarılı" : e.Level == LogLevel.Error ? "Hata" : "Tamam"),
                Level = e.Level
            })
            .ToList();
    }

    private static HealthScore TranslateHealth(HealthScore health)
    {
        var rating = health.Score switch
        {
            >= 90 => "Mükemmel",
            >= 75 => "İyi",
            >= 50 => "Orta",
            _ => "Dikkat gerekli"
        };

        var findings = health.Findings
            .Select(f => f
                .Replace("No issues detected.", "Sorun tespit edilmedi.")
                .Replace("not activated", "etkinleştirilmemiş")
                .Replace("Low free space", "Düşük boş alan")
                .Replace("pending", "bekliyor"))
            .ToList();

        return new HealthScore { Score = health.Score, Rating = rating, Findings = findings };
    }

    // ----- Yardımcılar ------------------------------------------------------

    private static string LayoutPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FendrSystemCare", "dashboard-layout.json");

    private static int QueryFirmwareType()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control");
            return key?.GetValue("PEFirmwareType") is int v ? v : 0;
        }
        catch { return 0; }
    }

    private static string QuerySecureBoot()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return key?.GetValue("UEFISecureBootEnabled") is int v && v == 1 ? "Açık" : "Kapalı";
        }
        catch { return "Bilinmiyor"; }
    }

    private static string QueryTpmVersion()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftTpm");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT SpecVersion FROM Win32_Tpm"));
            foreach (var o in searcher.Get())
                return Str(o["SpecVersion"]);
        }
        catch { }
        return "Yok / Bilinmiyor";
    }

    private static string QueryBitLocker()
    {
        try
        {
            var scope = new ManagementScope(@"\\.\root\CIMV2\Security\MicrosoftVolumeEncryption");
            scope.Connect();
            using var searcher = new ManagementObjectSearcher(scope,
                new ObjectQuery("SELECT ProtectionStatus, DriveLetter FROM Win32_EncryptableVolume"));
            var statuses = new List<string>();
            foreach (var o in searcher.Get())
            {
                var letter = Str(o["DriveLetter"]);
                var status = Int(o["ProtectionStatus"]);
                statuses.Add($"{letter}: {(status == 1 ? "Açık" : "Kapalı")}");
            }
            return statuses.Count > 0 ? string.Join(", ", statuses) : "Yapılandırılmamış";
        }
        catch { return "Bilinmiyor"; }
    }

    private static DashboardHealthLevel ScoreToLevel(int score) => score switch
    {
        >= 90 => DashboardHealthLevel.Excellent,
        >= 75 => DashboardHealthLevel.Good,
        >= 50 => DashboardHealthLevel.Warning,
        _ => DashboardHealthLevel.Critical
    };

    private static double ParseTempProgress(string temp)
    {
        if (temp == "Yok") return 0;
        if (double.TryParse(temp.Replace(" °C", ""), out var t))
            return Math.Clamp(t / 100.0 * 100, 0, 100);
        return 0;
    }

    private static DashboardHealthLevel ParseTempLevel(string temp)
    {
        if (temp == "Yok") return DashboardHealthLevel.Unknown;
        if (double.TryParse(temp.Replace(" °C", ""), out var t))
            return t switch { > 85 => DashboardHealthLevel.Critical, > 70 => DashboardHealthLevel.Warning, _ => DashboardHealthLevel.Good };
        return DashboardHealthLevel.Unknown;
    }

    private static string Str(object? v) => v?.ToString()?.Trim() ?? "Bilinmiyor";
    private static int Int(object? v) { try { return v is null ? 0 : Convert.ToInt32(v); } catch { return 0; } }

    private static string MapArchitecture(int arch) => arch switch
    {
        0 => "x86", 1 => "MIPS", 2 => "Alpha", 3 => "PowerPC", 5 => "ARM", 6 => "ia64",
        9 => "x64", 12 => "ARM64", _ => $"Arch {arch}"
    };

    private static string ParseWmiDate(object? value)
    {
        var raw = value?.ToString() ?? "";
        if (raw.Length >= 8) return $"{raw[..4]}-{raw[4..6]}-{raw[6..8]}";
        return "Bilinmiyor";
    }

    public void Dispose()
    {
        _monitoring.SampleAvailable -= OnMonitoringSample;
        _lhm?.Close();
    }
}
