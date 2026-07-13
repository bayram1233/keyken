using CommunityToolkit.Mvvm.ComponentModel;

namespace FendrSystemCare.Models;

/// <summary>
/// A program configured to run at logon, discovered from the registry Run keys
/// or the Startup folders. Enabling/disabling and delay are performed by
/// <c>IStartupService</c>.
/// </summary>
public sealed partial class StartupItem : ObservableObject
{
    public required string Name { get; init; }
    public required string Command { get; init; }
    public StartupLocation Location { get; init; }
    public StartupImpact Impact { get; init; }

    /// <summary>Publisher/company inferred from the target executable.</summary>
    public string Publisher { get; init; } = "Unknown";

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _delaySeconds;
}
