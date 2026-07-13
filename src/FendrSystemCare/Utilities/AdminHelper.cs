using System.Diagnostics;
using System.Security.Principal;

namespace FendrSystemCare.Utilities;

/// <summary>
/// Helpers for detecting and acquiring administrator privileges. The app
/// manifest already requests elevation, but this provides a defensive runtime
/// check and a graceful self-relaunch path should it ever run unelevated.
/// </summary>
public static class AdminHelper
{
    /// <summary>Returns true when the current process is running elevated.</summary>
    public static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to relaunch the current executable elevated via the UAC "runas"
    /// verb. Returns true when a new elevated instance was started (the caller
    /// should then shut itself down); false if the user declined or it failed.
    /// </summary>
    public static bool TryRelaunchAsAdministrator()
    {
        try
        {
            var executable = Environment.ProcessPath
                ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (string.IsNullOrEmpty(executable))
                return false;

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = true,
                Verb = "runas"
            };

            Process.Start(startInfo);
            return true;
        }
        catch
        {
            // User cancelled the UAC prompt or elevation is not available.
            return false;
        }
    }
}
