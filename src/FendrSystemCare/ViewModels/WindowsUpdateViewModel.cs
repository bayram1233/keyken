using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Windows Update page. Checks for, installs, pauses and resumes updates and
/// shows recent update history via the Windows Update agent.
/// </summary>
public sealed partial class WindowsUpdateViewModel : ViewModelBase
{
    private readonly IWindowsUpdateService _update;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;
    private CancellationTokenSource? _cts;

    public ObservableCollection<WindowsUpdateItem> Available { get; } = new();
    public ObservableCollection<WindowsUpdateItem> History { get; } = new();

    [ObservableProperty] private bool _hasChecked;

    public WindowsUpdateViewModel(IWindowsUpdateService update, IDialogService dialog, INotificationService notify)
    {
        Title = "Windows Update";
        _update = update;
        _dialog = dialog;
        _notify = notify;
    }

    public override async Task OnActivatedAsync() => await LoadHistory();

    [RelayCommand(CanExecute = nameof(NotBusy))]
    private async Task Check()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = "Checking for updates...";
        CheckCommand.NotifyCanExecuteChanged();
        try
        {
            Available.Clear();
            foreach (var item in await _update.CheckForUpdatesAsync(_cts.Token))
                Available.Add(item);
            HasChecked = true;
            _notify.ShowInfo("Update check complete", $"{Available.Count} update(s) available.");
        }
        catch (OperationCanceledException) { StatusMessage = "Cancelled."; }
        finally { Finish(); }
    }

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private async Task Install()
    {
        if (!_dialog.Confirm("Install updates", "Download and install all available updates now?"))
            return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        var progress = new Progress<string>(m => StatusMessage = m);
        CheckCommand.NotifyCanExecuteChanged();
        try
        {
            await _update.InstallUpdatesAsync(progress, _cts.Token);
            _notify.ShowSuccess("Updates installed", "A restart may be required to finish.");
            await LoadHistory();
        }
        catch (OperationCanceledException) { StatusMessage = "Cancelled."; }
        finally { Finish(); }
    }

    [RelayCommand]
    private async Task Pause()
    {
        await _update.PauseUpdatesAsync(7);
        _notify.ShowInfo("Updates paused", "Updates are paused for 7 days.");
    }

    [RelayCommand]
    private async Task Resume()
    {
        await _update.ResumeUpdatesAsync();
        _notify.ShowInfo("Updates resumed", "Windows Update is active again.");
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private async Task LoadHistory()
    {
        try
        {
            History.Clear();
            foreach (var item in await _update.GetHistoryAsync())
                History.Add(item);
        }
        catch { /* History is best-effort. */ }
    }

    private bool NotBusy() => !IsBusy;
    private bool CanInstall() => !IsBusy && Available.Count > 0;

    private void Finish()
    {
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
        CheckCommand.NotifyCanExecuteChanged();
        InstallCommand.NotifyCanExecuteChanged();
    }
}
