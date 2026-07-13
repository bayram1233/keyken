using System.Windows.Media;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using Wpf.Ui.Appearance;

namespace FendrSystemCare.Services;

/// <summary>
/// Applies the Fluent light/dark theme and accent colour at runtime using the
/// WPF-UI appearance manager, which also drives the Mica backdrop tint.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private readonly ILoggingService _log;

    public AppTheme CurrentTheme { get; private set; } = AppTheme.Dark;

    public ThemeService(ILoggingService log) => _log = log;

    public void Apply(AppTheme theme, string accentColor)
    {
        CurrentTheme = theme;

        try
        {
            // Accent first so theme application picks it up.
            if (!string.IsNullOrWhiteSpace(accentColor) &&
                ColorConverter.ConvertFromString(accentColor) is Color color)
            {
                ApplicationAccentColorManager.Apply(color, GetApplicationTheme(theme));
            }

            switch (theme)
            {
                case AppTheme.Light:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light);
                    break;
                case AppTheme.Dark:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark);
                    break;
                default:
                    ApplicationThemeManager.ApplySystemTheme();
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Error("Failed to apply theme.", ex);
        }
    }

    private static ApplicationTheme GetApplicationTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ApplicationTheme.Light,
        AppTheme.Dark => ApplicationTheme.Dark,
        _ => ApplicationThemeManager.GetSystemTheme() == SystemTheme.Light
            ? ApplicationTheme.Light
            : ApplicationTheme.Dark
    };
}
