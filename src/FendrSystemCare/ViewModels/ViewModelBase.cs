using CommunityToolkit.Mvvm.ComponentModel;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Base class for all view-models. Provides a title, a shared busy flag and an
/// overridable activation hook the shell calls when the page is navigated to.
/// </summary>
public abstract partial class ViewModelBase : ObservableObject
{
    /// <summary>Human-readable page title shown in the header.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>True while a long-running operation is in progress.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Short status line shown while <see cref="IsBusy"/> is true.</summary>
    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// Called by the shell when the page becomes active. Override to load data.
    /// The default implementation does nothing.
    /// </summary>
    public virtual Task OnActivatedAsync() => Task.CompletedTask;
}
