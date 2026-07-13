using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Settings page. Edits and persists <see cref="AppSettings"/> and applies the
/// theme/accent immediately so the user sees changes live.
/// </summary>
public sealed partial class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ILoggingService _log;
    private readonly INotificationService _notify;

    public IReadOnlyList<AppTheme> Themes { get; } = new[] { AppTheme.System, AppTheme.Light, AppTheme.Dark };

    public ObservableCollection<string> AccentColors { get; } = new()
    {
        "#0A84FF", "#30D158", "#FF453A", "#FF9F0A", "#BF5AF2", "#64D2FF", "#FF375F"
    };

    public IReadOnlyList<string> Languages { get; } = new[] { "en", "tr" };

    [ObservableProperty] private AppTheme _selectedTheme;
    [ObservableProperty] private string _selectedAccent = "#0A84FF";
    [ObservableProperty] private string _selectedLanguage = "en";
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _autoScan;
    [ObservableProperty] private bool _createRestorePointAutomatically;
    [ObservableProperty] private bool _notificationsEnabled;
    [ObservableProperty] private bool _minimizeToTray;

    public SettingsViewModel(ISettingsService settings, IThemeService theme,
        ILoggingService log, INotificationService notify)
    {
        Title = "Settings";
        _settings = settings;
        _theme = theme;
        _log = log;
        _notify = notify;

        var s = _settings.Current;
        _selectedTheme = s.Theme;
        _selectedAccent = s.AccentColor;
        _selectedLanguage = s.Language;
        _autoUpdate = s.AutoUpdate;
        _autoScan = s.AutoScan;
        _createRestorePointAutomatically = s.CreateRestorePointAutomatically;
        _notificationsEnabled = s.NotificationsEnabled;
        _minimizeToTray = s.MinimizeToTray;
    }

    // Apply the theme immediately whenever the user picks a new theme/accent.
    partial void OnSelectedThemeChanged(AppTheme value) => _theme.Apply(value, SelectedAccent);
    partial void OnSelectedAccentChanged(string value) => _theme.Apply(SelectedTheme, value);

    [RelayCommand]
    private async Task Save()
    {
        var s = _settings.Current;
        s.Theme = SelectedTheme;
        s.AccentColor = SelectedAccent;
        s.Language = SelectedLanguage;
        s.AutoUpdate = AutoUpdate;
        s.AutoScan = AutoScan;
        s.CreateRestorePointAutomatically = CreateRestorePointAutomatically;
        s.NotificationsEnabled = NotificationsEnabled;
        s.MinimizeToTray = MinimizeToTray;

        await _settings.SaveAsync();
        _theme.Apply(SelectedTheme, SelectedAccent);
        _notify.ShowSuccess("Settings saved", "Your preferences have been applied.");
    }

    [RelayCommand]
    private void OpenLogs()
    {
        try
        {
            Process.Start(new ProcessStartInfo(_log.LogFilePath) { UseShellExecute = true });
        }
        catch
        {
            _notify.ShowWarning("Logs", "Could not open the log file.");
        }
    }
}
