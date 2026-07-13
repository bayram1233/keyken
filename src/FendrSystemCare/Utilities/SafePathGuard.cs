using System.IO;

namespace FendrSystemCare.Utilities;

/// <summary>
/// Guards deletion operations so the cleanup engine can never touch user data
/// or critical system locations. A path is only considered deletable when it is
/// NOT inside any protected root. This is a hard safety net independent of which
/// categories are configured.
/// </summary>
public static class SafePathGuard
{
    private static readonly string[] ProtectedRoots = BuildProtectedRoots();

    /// <summary>
    /// Returns true only when <paramref name="path"/> is safe to delete, i.e. it
    /// does not fall within a user-content or system-critical folder.
    /// </summary>
    public static bool IsSafeToDelete(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return false;

        string full;
        try { full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch { return false; }

        foreach (var root in ProtectedRoots)
        {
            if (root.Length == 0) continue;

            // Block the protected root itself and anything beneath it.
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase))
                return false;
            if (full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string[] BuildProtectedRoots()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);

        var roots = new List<string?>
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
            Environment.GetFolderPath(Environment.SpecialFolder.Favorites),
            // Downloads has no SpecialFolder entry; derive it from the profile.
            string.IsNullOrEmpty(profile) ? null : Path.Combine(profile, "Downloads"),
            // Critical system locations that must never be cleaned.
            string.IsNullOrEmpty(system) ? null : system,
            string.IsNullOrEmpty(windows) ? null : Path.Combine(windows, "System32", "config"),
            string.IsNullOrEmpty(windows) ? null : Path.Combine(windows, "System32", "drivers"),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        };

        return roots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => Path.GetFullPath(r!).TrimEnd(Path.DirectorySeparatorChar))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
