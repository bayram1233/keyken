using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Reflection;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Shell view-model. Owns the sidebar navigation list and the currently active
/// page view-model. Page view-models are resolved from the DI container on
/// demand so each navigation gives a fresh, data-loaded page.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IServiceProvider _services;
    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;

    /// <summary>The ordered list of sidebar destinations.</summary>
    public ObservableCollection<NavigationItem> NavigationItems { get; }

    /// <summary>Filtered/sortable view over <see cref="NavigationItems"/> for the search box.</summary>
    public ICollectionView NavigationView { get; }

    /// <summary>The view-model currently shown in the content area.</summary>
    [ObservableProperty]
    private ViewModelBase? _currentPage;

    [ObservableProperty]
    private NavigationItem? _selectedItem;

    /// <summary>Live text used to filter the sidebar destinations.</summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>Whether the sidebar shows labels (expanded) or icons only (collapsed).</summary>
    [ObservableProperty]
    private bool _isSidebarExpanded = true;

    /// <summary>Product version string shown in the status bar.</summary>
    public string AppVersion { get; }

    public MainViewModel(IServiceProvider services, INavigationService navigation,
        IThemeService theme, ISettingsService settings)
    {
        _services = services;
        _navigation = navigation;
        _theme = theme;
        _settings = settings;

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        AppVersion = version is null ? "1.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";

        NavigationItems = new ObservableCollection<NavigationItem>
        {
            new("Dashboard", "Home24", typeof(DashboardViewModel), "Overview"),
            new("Driver Center", "DeveloperBoard24", typeof(DriverCenterViewModel), "Maintenance"),
            new("System Repair", "WrenchScrewdriver24", typeof(SystemRepairViewModel), "Maintenance"),
            new("Cleanup", "Broom24", typeof(CleanupViewModel), "Maintenance"),
            new("Performance", "TopSpeed24", typeof(PerformanceViewModel), "Maintenance"),
            new("Startup Manager", "Rocket24", typeof(StartupViewModel), "Maintenance"),
            new("Windows Update", "ArrowSync24", typeof(WindowsUpdateViewModel), "Maintenance"),
            new("Tools", "Toolbox24", typeof(ToolsViewModel), "Advanced"),
            new("Settings", "Settings24", typeof(SettingsViewModel), "App"),
            new("About", "Info24", typeof(AboutViewModel), "App")
        };

        NavigationView = CollectionViewSource.GetDefaultView(NavigationItems);
        NavigationView.Filter = FilterNavigationItem;
        NavigationView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavigationItem.Group)));

        _navigation.Navigated += (_, type) => Navigate(type);
    }

    /// <summary>Collapses/expands the sidebar (labels vs icon-only).</summary>
    [RelayCommand]
    private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

    /// <summary>Switches between light and dark themes and persists the choice.</summary>
    [RelayCommand]
    private async Task ToggleThemeAsync()
    {
        var next = _theme.CurrentTheme == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _theme.Apply(next, _settings.Current.AccentColor);
        _settings.Current.Theme = next;
        await _settings.SaveAsync();
    }

    private bool FilterNavigationItem(object item)
    {
        if (string.IsNullOrWhiteSpace(SearchText)) return true;
        return item is NavigationItem nav &&
               nav.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnSearchTextChanged(string value) => NavigationView.Refresh();

    /// <summary>Activates the first destination once the shell is loaded.</summary>
    public void Initialize() => Navigate(typeof(DashboardViewModel));

    [RelayCommand]
    private void Select(NavigationItem? item)
    {
        if (item is not null) Navigate(item.ViewModelType);
    }

    /// <summary>Navigates to the nth destination (used by Ctrl+1..9 shortcuts).</summary>
    [RelayCommand]
    private void NavigateIndex(string? indexText)
    {
        if (!int.TryParse(indexText, out var index)) return;
        index -= 1; // Shortcuts are 1-based for the user.
        if (index >= 0 && index < NavigationItems.Count)
            Navigate(NavigationItems[index].ViewModelType);
    }

    /// <summary>Reloads the current page's data (F5).</summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (CurrentPage is not null)
            await CurrentPage.OnActivatedAsync();
    }

    // Navigate when the bound ListBox selection changes, skipping redundant
    // navigations to the page that is already showing.
    partial void OnSelectedItemChanged(NavigationItem? value)
    {
        if (value is not null && CurrentPage?.GetType() != value.ViewModelType)
            Navigate(value.ViewModelType);
    }

    private async void Navigate(Type viewModelType)
    {
        // Dispose the outgoing page so it unsubscribes from live services, then
        // resolve the target page from DI and let it load its data.
        if (CurrentPage is IDisposable disposable)
            disposable.Dispose();

        var page = (ViewModelBase)_services.GetRequiredService(viewModelType);
        CurrentPage = page;

        foreach (var item in NavigationItems)
            item.IsSelected = item.ViewModelType == viewModelType;
        SelectedItem = NavigationItems.FirstOrDefault(i => i.ViewModelType == viewModelType);

        await page.OnActivatedAsync();
    }
}
