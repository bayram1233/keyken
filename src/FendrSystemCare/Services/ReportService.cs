using System.IO;
using System.Text;
using System.Text.Json;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.Services;

/// <summary>
/// Builds JSON, HTML and PDF reports of the current system state and health.
/// PDF is produced by rendering the HTML report through Edge's headless
/// print-to-PDF engine (present on all supported Windows versions).
/// </summary>
public sealed class ReportService : IReportService
{
    private readonly ISystemInfoService _systemInfo;
    private readonly ILoggingService _log;

    public ReportService(ISystemInfoService systemInfo, ILoggingService log)
    {
        _systemInfo = systemInfo;
        _log = log;
    }

    public async Task<string> GenerateAsync(ReportFormat format, string destinationFolder, CancellationToken ct = default)
    {
        Directory.CreateDirectory(destinationFolder);

        var info = await _systemInfo.GetSystemInformationAsync(ct).ConfigureAwait(false);
        var health = await _systemInfo.ComputeHealthScoreAsync(ct).ConfigureAwait(false);
        var volumes = await _systemInfo.GetStorageVolumesAsync(ct).ConfigureAwait(false);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        return format switch
        {
            ReportFormat.Json => await WriteJsonAsync(info, health, volumes, destinationFolder, stamp, ct).ConfigureAwait(false),
            ReportFormat.Html => await WriteHtmlAsync(info, health, volumes, destinationFolder, stamp, ct).ConfigureAwait(false),
            _ => await WritePdfAsync(info, health, volumes, destinationFolder, stamp, ct).ConfigureAwait(false)
        };
    }

    private async Task<string> WriteJsonAsync(SystemInformation info, HealthScore health,
        IReadOnlyList<StorageVolume> volumes, string folder, string stamp, CancellationToken ct)
    {
        var path = Path.Combine(folder, $"FendrReport-{stamp}.json");
        var payload = new
        {
            GeneratedAt = DateTime.Now,
            Machine = info.MachineName,
            Health = health,
            System = info,
            Storage = volumes
        };
        await using var stream = File.Create(path);
        await JsonSerializer.SerializeAsync(stream, payload, new JsonSerializerOptions { WriteIndented = true }, ct).ConfigureAwait(false);
        _log.Success("JSON report generated.", path);
        return path;
    }

    private async Task<string> WriteHtmlAsync(SystemInformation info, HealthScore health,
        IReadOnlyList<StorageVolume> volumes, string folder, string stamp, CancellationToken ct)
    {
        var path = Path.Combine(folder, $"FendrReport-{stamp}.html");
        await File.WriteAllTextAsync(path, BuildHtml(info, health, volumes), Encoding.UTF8, ct).ConfigureAwait(false);
        _log.Success("HTML report generated.", path);
        return path;
    }

    private async Task<string> WritePdfAsync(SystemInformation info, HealthScore health,
        IReadOnlyList<StorageVolume> volumes, string folder, string stamp, CancellationToken ct)
    {
        var pdfPath = Path.Combine(folder, $"FendrReport-{stamp}.pdf");
        var tempHtml = Path.Combine(Path.GetTempPath(), $"FendrReport-{stamp}.html");
        await File.WriteAllTextAsync(tempHtml, BuildHtml(info, health, volumes), Encoding.UTF8, ct).ConfigureAwait(false);

        var edge = FindEdge();
        if (edge is not null)
        {
            var args = $"--headless=new --disable-gpu --no-margins --print-to-pdf=\"{pdfPath}\" \"{tempHtml}\"";
            var result = await ProcessRunner.RunAsync(edge, args, null, ct).ConfigureAwait(false);
            if (File.Exists(pdfPath))
            {
                _log.Success("PDF report generated.", pdfPath);
                TryDelete(tempHtml);
                return pdfPath;
            }
            _log.Warning("Edge PDF conversion failed; keeping HTML report.", result.Output);
        }

        // Fall back to the HTML report when no PDF engine is available.
        _log.Info("PDF engine unavailable; produced HTML report instead.");
        return tempHtml;
    }

    private static string BuildHtml(SystemInformation info, HealthScore health, IReadOnlyList<StorageVolume> volumes)
    {
        var sb = new StringBuilder();
        sb.Append("""
            <!DOCTYPE html><html><head><meta charset="utf-8"><title>Fendr System Care Report</title>
            <style>
            body{font-family:'Segoe UI',Arial,sans-serif;background:#f4f6fb;color:#1a1a1a;margin:40px;}
            h1{color:#0a84ff;} h2{border-bottom:2px solid #0a84ff;padding-bottom:6px;margin-top:32px;}
            .score{font-size:64px;font-weight:700;}
            table{border-collapse:collapse;width:100%;margin-top:12px;}
            td,th{border:1px solid #d0d5dd;padding:8px 12px;text-align:left;}
            th{background:#0a84ff;color:#fff;}
            .card{background:#fff;border-radius:12px;padding:24px;box-shadow:0 2px 8px rgba(0,0,0,.06);}
            </style></head><body>
            """);
        sb.Append("<div class='card'><h1>Fendr System Care</h1>");
        sb.Append($"<p>Generated {DateTime.Now:f} on <b>{Html(info.MachineName)}</b></p>");
        sb.Append($"<div class='score'>{health.Score}<span style='font-size:24px'>/100</span></div>");
        sb.Append($"<p><b>{Html(health.Rating)}</b></p><ul>");
        foreach (var f in health.Findings) sb.Append($"<li>{Html(f)}</li>");
        sb.Append("</ul></div>");

        sb.Append("<h2>System</h2><table>");
        Row(sb, "Windows", $"{info.WindowsEdition} {info.WindowsVersion} (Build {info.WindowsBuild})");
        Row(sb, "Activation", info.ActivationStatus);
        Row(sb, "CPU", $"{info.CpuName} — {info.CpuCores}C/{info.CpuLogicalProcessors}T @ {info.CpuMaxClockGhz} GHz");
        Row(sb, "GPU", $"{info.GpuName} ({info.GpuMemoryGb} GB, driver {info.GpuDriverVersion})");
        Row(sb, "RAM", $"{info.TotalRamGb} GB {info.RamType} @ {info.RamSpeedMhz} MHz");
        Row(sb, "Motherboard", $"{info.MotherboardManufacturer} {info.MotherboardProduct}");
        Row(sb, "BIOS", $"{info.BiosVersion} ({info.BiosDate})");
        sb.Append("</table>");

        sb.Append("<h2>Storage</h2><table><tr><th>Drive</th><th>Label</th><th>Used</th><th>Free</th><th>Total</th></tr>");
        foreach (var v in volumes)
            sb.Append($"<tr><td>{Html(v.Drive)}</td><td>{Html(v.Label)}</td><td>{v.UsedGb:0.#} GB ({v.UsedPercent}%)</td><td>{v.FreeGb:0.#} GB</td><td>{v.TotalGb:0.#} GB</td></tr>");
        sb.Append("</table>");

        sb.Append("<h2>Disk Health</h2><table><tr><th>Model</th><th>Type</th><th>Size</th><th>Health</th></tr>");
        foreach (var d in info.Disks)
            sb.Append($"<tr><td>{Html(d.Model)}</td><td>{Html(d.MediaType)}</td><td>{d.SizeGb:0} GB</td><td>{Html(d.HealthStatus)}</td></tr>");
        sb.Append("</table></body></html>");
        return sb.ToString();
    }

    private static void Row(StringBuilder sb, string label, string value) =>
        sb.Append($"<tr><th style='width:220px'>{Html(label)}</th><td>{Html(value)}</td></tr>");

    private static string Html(string value) => System.Net.WebUtility.HtmlEncode(value);

    private static string? FindEdge()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "Edge", "Application", "msedge.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "Edge", "Application", "msedge.exe")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* Ignore. */ }
    }
}
