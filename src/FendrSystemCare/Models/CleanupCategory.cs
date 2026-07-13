using CommunityToolkit.Mvvm.ComponentModel;

namespace FendrSystemCare.Models;

/// <summary>
/// A single cleanable location (e.g. "User Temp"). It is observable so the
/// cleanup view can live-update the discovered size and selection state.
/// </summary>
public sealed partial class CleanupCategory : ObservableObject
{
    /// <summary>Stable identifier used by the cleanup service.</summary>
    public string Key { get; }

    /// <summary>Display name shown in the UI.</summary>
    public string Name { get; }

    /// <summary>Short description of what will be removed.</summary>
    public string Description { get; }

    /// <summary>WPF-UI symbol name for the category icon.</summary>
    public string Icon { get; }

    /// <summary>Absolute paths scanned/cleaned for this category.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Whether this category is included in a clean operation.</summary>
    [ObservableProperty]
    private bool _isSelected = true;

    /// <summary>Bytes discovered during the last scan.</summary>
    [ObservableProperty]
    private long _sizeBytes;

    /// <summary>True while this category is being scanned or cleaned.</summary>
    [ObservableProperty]
    private bool _isBusy;

    public CleanupCategory(string key, string name, string description, string icon, IReadOnlyList<string> paths)
    {
        Key = key;
        Name = name;
        Description = description;
        Icon = icon;
        Paths = paths;
    }
}
