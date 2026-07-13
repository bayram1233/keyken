using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.Input;

namespace FendrSystemCare.ViewModels;

/// <summary>About page. Shows product/version details and support links.</summary>
public sealed partial class AboutViewModel : ViewModelBase
{
    public string ProductName => "Fendr System Care";
    public string Version => Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";
    public string Framework => ".NET 8 · WPF · Fluent Design";
    public string Copyright => $"© {DateTime.Now.Year} Fendr";
    public string Description =>
        "A premium Windows maintenance suite: driver management, system repair, cleanup, performance tuning and real-time monitoring.";

    public AboutViewModel() => Title = "About";

    [RelayCommand]
    private void OpenLink(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* Ignore invalid links. */ }
    }
}
