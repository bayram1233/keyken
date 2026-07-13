using System.IO;
using System.Runtime.InteropServices;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Scans and cleans temporary, cache and log locations. Categories are
/// path-based (safe recursive delete of locked-file-tolerant contents) except
/// the Recycle Bin which uses the shell API. Nothing is deleted until the user
/// runs a clean; scanning is read-only.
/// </summary>
public sealed class CleanupService : ICleanupService
{
    private readonly ILoggingService _log;
    private readonly List<CleanupCategory> _categories;

    private const string RecycleBinKey = "recycle_bin";

    public CleanupService(ILoggingService log)
    {
        _log = log;
        _categories = BuildCategories();
    }

    public IReadOnlyList<CleanupCategory> GetCategories() => _categories;

    public async Task ScanAsync(IEnumerable<CleanupCategory> categories, CancellationToken ct = default)
    {
        foreach (var category in categories)
        {
            ct.ThrowIfCancellationRequested();
            category.IsBusy = true;
            try
            {
                category.SizeBytes = category.Key == RecycleBinKey
                    ? await Task.Run(GetRecycleBinSize, ct).ConfigureAwait(false)
                    : await Task.Run(() => MeasureSize(category, ct), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warning($"Scan failed for '{category.Name}'.", ex.Message);
                category.SizeBytes = 0;
            }
            finally
            {
                category.IsBusy = false;
            }
        }
    }

    public async Task<long> CleanAsync(IEnumerable<CleanupCategory> categories, IProgress<string>? progress, CancellationToken ct = default)
    {
        long freed = 0;
        foreach (var category in categories)
        {
            ct.ThrowIfCancellationRequested();
            if (!category.IsSelected) continue;

            category.IsBusy = true;
            progress?.Report($"Cleaning {category.Name}...");
            try
            {
                var before = category.SizeBytes;
                if (category.Key == RecycleBinKey)
                {
                    await Task.Run(EmptyRecycleBin, ct).ConfigureAwait(false);
                    freed += before;
                }
                else
                {
                    freed += await Task.Run(() => DeleteContents(category, ct), ct).ConfigureAwait(false);
                }

                category.SizeBytes = 0;
                _log.Success($"Cleaned {category.Name}.", FormatHelper.Bytes(before));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warning($"Clean failed for '{category.Name}'.", ex.Message);
            }
            finally
            {
                category.IsBusy = false;
            }
        }

        progress?.Report($"Done. Recovered {FormatHelper.Bytes(freed)}.");
        return freed;
    }

    // ----- size / delete helpers -------------------------------------------

    private static long MeasureSize(CleanupCategory category, CancellationToken ct)
    {
        long total = 0;
        foreach (var file in EnumerateFiles(category.Paths))
        {
            ct.ThrowIfCancellationRequested();
            try { total += new FileInfo(file).Length; }
            catch { /* Skip files we cannot stat. */ }
        }
        return total;
    }

    private long DeleteContents(CleanupCategory category, CancellationToken ct)
    {
        long freed = 0;
        foreach (var file in EnumerateFiles(category.Paths))
        {
            ct.ThrowIfCancellationRequested();

            // Hard safety net: never delete anything inside a protected user or
            // system location, regardless of how the category was configured.
            if (!SafePathGuard.IsSafeToDelete(file))
            {
                _log.Warning("Skipped protected path during cleanup.", file);
                continue;
            }

            try
            {
                var size = new FileInfo(file).Length;
                File.SetAttributes(file, FileAttributes.Normal);
                File.Delete(file);
                freed += size;
            }
            catch { /* Locked/in-use files are skipped silently. */ }
        }

        // Remove now-empty sub-directories for directory-based categories.
        foreach (var spec in category.Paths)
        {
            if (spec.Contains('*') || File.Exists(spec)) continue;
            TryRemoveEmptyDirectories(spec, ct);
        }

        return freed;
    }

    private static IEnumerable<string> EnumerateFiles(IEnumerable<string> pathSpecs)
    {
        foreach (var spec in pathSpecs)
        {
            var expanded = Environment.ExpandEnvironmentVariables(spec);

            if (expanded.Contains('*'))
            {
                var dir = Path.GetDirectoryName(expanded);
                var pattern = Path.GetFileName(expanded);
                if (dir is null || !Directory.Exists(dir)) continue;
                foreach (var f in SafeEnumerate(dir, pattern, SearchOption.AllDirectories))
                    yield return f;
            }
            else if (File.Exists(expanded))
            {
                yield return expanded;
            }
            else if (Directory.Exists(expanded))
            {
                foreach (var f in SafeEnumerate(expanded, "*", SearchOption.AllDirectories))
                    yield return f;
            }
        }
    }

    private static IEnumerable<string> SafeEnumerate(string dir, string pattern, SearchOption option)
    {
        // EnumerateFiles can throw part-way through on access-denied; wrap the
        // whole enumeration and return what we managed to read.
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir, pattern, option); }
        catch { yield break; }

        foreach (var f in files)
            yield return f;
    }

    private void TryRemoveEmptyDirectories(string root, CancellationToken ct)
    {
        var expanded = Environment.ExpandEnvironmentVariables(root);
        if (!Directory.Exists(expanded)) return;

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(expanded, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    if (!Directory.EnumerateFileSystemEntries(dir).Any())
                        Directory.Delete(dir);
                }
                catch { /* Non-empty or locked. */ }
            }
        }
        catch { /* Best effort cleanup. */ }
    }

    // ----- recycle bin ------------------------------------------------------

    private static long GetRecycleBinSize()
    {
        var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
        return NativeMethods.SHQueryRecycleBin(null, ref info) == 0 ? info.i64Size : 0;
    }

    private void EmptyRecycleBin()
    {
        var hr = NativeMethods.SHEmptyRecycleBin(IntPtr.Zero, null, NativeMethods.SHERB_SILENT);
        if (hr != 0)
            _log.Warning("Emptying the Recycle Bin returned a non-zero result.", hr.ToString());
    }

    // ----- category catalogue ----------------------------------------------

    private static List<CleanupCategory> BuildCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var temp = Path.GetTempPath();

        return new List<CleanupCategory>
        {
            new("user_temp", "User Temp", "Per-user temporary files.", "Folder24",
                new[] { temp, Path.Combine(local, "Temp") }),
            new("windows_temp", "Windows Temp", "System temporary files.", "Folder24",
                new[] { Path.Combine(windir, "Temp") }),
            new("windows_update", "Windows Update Cache", "Downloaded update packages.", "ArrowDownload24",
                new[] { Path.Combine(windir, "SoftwareDistribution", "Download") }),
            new("directx_cache", "DirectX Shader Cache", "Compiled shader cache.", "DeveloperBoard24",
                new[] { Path.Combine(local, "D3DSCache") }),
            new("thumbnails", "Thumbnail Cache", "Explorer thumbnail database.", "Image24",
                new[] { Path.Combine(local, "Microsoft", "Windows", "Explorer", "thumbcache_*.db") }),
            new(RecycleBinKey, "Recycle Bin", "All deleted files across drives.", "Delete24",
                Array.Empty<string>()),
            new("crash_dumps", "Crash Dumps", "Application crash dump files.", "Bug24",
                new[] { Path.Combine(local, "CrashDumps") }),
            new("memory_dumps", "Memory Dumps", "Kernel and mini memory dumps.", "Bug24",
                new[] { Path.Combine(windir, "Minidump"), Path.Combine(windir, "memory.dmp") }),
            new("edge_cache", "Edge Cache", "Microsoft Edge browser cache.", "Globe24",
                new[] { Path.Combine(local, "Microsoft", "Edge", "User Data", "Default", "Cache") }),
            new("chrome_cache", "Chrome Cache", "Google Chrome browser cache.", "Globe24",
                new[] { Path.Combine(local, "Google", "Chrome", "User Data", "Default", "Cache") }),
            new("firefox_cache", "Firefox Cache", "Mozilla Firefox browser cache.", "Globe24",
                new[] { Path.Combine(local, "Mozilla", "Firefox", "Profiles") }),
            new("prefetch", "Prefetch", "Windows prefetch data.", "Flash24",
                new[] { Path.Combine(windir, "Prefetch") }),
            new("logs", "Logs", "System and setup log files.", "Document24",
                new[] { Path.Combine(windir, "Logs"), Path.Combine(local, "Temp", "*.log") }),
            new("delivery_optimization", "Delivery Optimization Cache", "P2P update cache.", "CloudArrowDown24",
                new[] { Path.Combine(windir, "ServiceProfiles", "NetworkService", "AppData", "Local", "Microsoft", "Windows", "DeliveryOptimization", "Cache") }),
            new("wer", "Windows Error Reports", "Queued error report data.", "Warning24",
                new[] { Path.Combine(local, "Microsoft", "Windows", "WER"), Path.Combine(programData, "Microsoft", "Windows", "WER") }),
            new("old_windows", "Old Windows Installations", "Previous Windows (Windows.old).", "History24",
                new[] { Path.Combine(Path.GetPathRoot(windir) ?? "C:\\", "Windows.old") })
        };
    }
}
