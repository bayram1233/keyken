using System.Diagnostics;
using System.Text;

namespace FendrSystemCare.Utilities;

/// <summary>
/// Small helper that runs an external process asynchronously, streams its
/// combined stdout/stderr through an <see cref="IProgress{T}"/>, honours a
/// <see cref="CancellationToken"/> (killing the tree on cancel) and returns the
/// exit code plus captured output.
/// </summary>
public static class ProcessRunner
{
    public sealed record Result(int ExitCode, string Output);

    /// <summary>
    /// Starts <paramref name="fileName"/> with <paramref name="arguments"/> and
    /// awaits completion. Output lines are reported live via <paramref name="output"/>.
    /// </summary>
    public static async Task<Result> RunAsync(
        string fileName,
        string arguments,
        IProgress<string>? output = null,
        CancellationToken ct = default)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var sb = new StringBuilder();

        void OnData(string? line)
        {
            if (line is null) return;
            sb.AppendLine(line);
            output?.Report(line);
        }

        process.OutputDataReceived += (_, e) => OnData(e.Data);
        process.ErrorDataReceived += (_, e) => OnData(e.Data);

        if (!process.Start())
            throw new InvalidOperationException($"Failed to start process '{fileName}'.");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        return new Result(process.ExitCode, sb.ToString());
    }

    /// <summary>Runs a command and returns only its captured output.</summary>
    public static async Task<string> CaptureAsync(string fileName, string arguments, CancellationToken ct = default)
    {
        var result = await RunAsync(fileName, arguments, null, ct).ConfigureAwait(false);
        return result.Output;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Process may have exited between the check and the kill call.
        }
    }
}
