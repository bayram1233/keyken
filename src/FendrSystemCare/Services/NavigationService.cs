using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.Services;

/// <summary>
/// Lightweight navigation coordinator. It does not create views directly;
/// instead it announces the requested view-model type and the shell view-model
/// swaps its current page accordingly (ViewModel-first navigation).
/// </summary>
public sealed class NavigationService : INavigationService
{
    public event EventHandler<Type>? Navigated;

    public void NavigateTo(Type viewModelType) => Navigated?.Invoke(this, viewModelType);

    public void NavigateTo<TViewModel>() where TViewModel : class => NavigateTo(typeof(TViewModel));
}
