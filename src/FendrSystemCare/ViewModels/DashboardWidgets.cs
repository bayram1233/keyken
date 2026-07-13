using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// A summary tile on the dashboard. Values are observable so live metrics
/// (memory, temperature, network) can refresh the same card in place.
/// </summary>
public sealed partial class DashboardCard : ObservableObject
{
    public required string Title { get; init; }
    public required string Icon { get; init; }

    [ObservableProperty] private string _value = "-";
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private Brush _accent = StatusPalette.Info;

    /// <summary>0-100 value driving the card's progress ring.</summary>
    [ObservableProperty] private double _progress;

    /// <summary>Whether the progress ring should be shown for this card.</summary>
    public bool ShowProgress { get; init; }

    public string? ActionLabel { get; init; }
    public IRelayCommand? Action { get; init; }
    public bool HasAction => Action is not null && ActionLabel is not null;
}

/// <summary>A colour-coded alert shown in the dashboard alert centre.</summary>
public sealed class AlertItem
{
    public required string Message { get; init; }
    public string Icon { get; init; } = "Warning24";
    public Brush Accent { get; init; } = StatusPalette.Warning;
}

/// <summary>A single explained performance meter (CPU, memory, storage, ...).</summary>
public sealed class PerformanceScoreItem
{
    public required string Name { get; init; }
    public required int Score { get; init; }
    public required string Explanation { get; init; }
    public Brush Accent => StatusPalette.ForScore(Score);
}
