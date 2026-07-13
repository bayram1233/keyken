using CommunityToolkit.Mvvm.ComponentModel;

namespace FendrSystemCare.ViewModels;

/// <summary>A single entry in the sidebar navigation list.</summary>
public sealed partial class NavigationItem : ObservableObject
{
    public string Title { get; }

    /// <summary>WPF-UI symbol name rendered as the item's glyph.</summary>
    public string Icon { get; }

    /// <summary>The view-model type navigated to when this item is selected.</summary>
    public Type ViewModelType { get; }

    /// <summary>Sidebar section this entry belongs to (used for grouping headers).</summary>
    public string Group { get; }

    /// <summary>True when this is the active page (drives the selection style).</summary>
    [ObservableProperty]
    private bool _isSelected;

    public NavigationItem(string title, string icon, Type viewModelType, string group = "General")
    {
        Title = title;
        Icon = icon;
        ViewModelType = viewModelType;
        Group = group;
    }
}
