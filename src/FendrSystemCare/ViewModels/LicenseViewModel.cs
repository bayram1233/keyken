using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.ViewModels;

public sealed partial class LicenseViewModel : ObservableObject
{
    private readonly ILicenseService _license;
    private readonly Action _onSuccess;

    [ObservableProperty] private string _apiKey = string.Empty;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private bool _hasError;

    public string ActivationUrl => _license.ActivationUrl;

    public LicenseViewModel(ILicenseService license, Action onSuccess)
    {
        _license = license;
        _onSuccess = onSuccess;
        if (_license.StoredKey is not null) ApiKey = _license.StoredKey;
    }

    [RelayCommand]
    private void Activate()
    {
        HasError = false;
        if (_license.ValidateAndSave(ApiKey))
        {
            _onSuccess();
            return;
        }
        HasError = true;
        ErrorMessage = "Geçersiz API anahtarı. Lütfen web sitesinden anahtar alın.";
    }

    [RelayCommand]
    private void OpenActivationSite()
    {
        Process.Start(new ProcessStartInfo(_license.ActivationUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void PasteMasterHint()
    {
        // Kullanıcıya format ipucu — master key doğrudan yapıştırılabilir
        ApiKey = string.Empty;
        ErrorMessage = "Format: FENDR-XXXXXXXX-CCCC";
        HasError = false;
    }
}
