using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Startup Manager page. Lists logon startup entries with their publisher and
/// estimated impact, and allows enabling, disabling, delaying or removing each.
/// </summary>
public sealed partial class StartupViewModel : ViewModelBase
{
    private readonly IStartupService _startup;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;

    public ObservableCollection<StartupItem> Items { get; } = new();

    public StartupViewModel(IStartupService startup, IDialogService dialog, INotificationService notify)
    {
        Title = "Startup Manager";
        _startup = startup;
        _dialog = dialog;
        _notify = notify;
    }

    public override async Task OnActivatedAsync() => await Refresh();

    [RelayCommand]
    private async Task Refresh()
    {
        IsBusy = true;
        StatusMessage = "Reading startup entries...";
        try
        {
            Items.Clear();
            foreach (var item in await _startup.GetStartupItemsAsync())
                Items.Add(item);
        }
        finally { IsBusy = false; StatusMessage = string.Empty; }
    }

    [RelayCommand]
    private async Task Toggle(StartupItem? item)
    {
        if (item is null) return;
        await _startup.SetEnabledAsync(item, !item.IsEnabled);
    }

    [RelayCommand]
    private async Task Remove(StartupItem? item)
    {
        if (item is null) return;
        if (!_dialog.Confirm("Remove startup item", $"Permanently remove '{item.Name}' from startup?"))
            return;

        await _startup.RemoveAsync(item);
        Items.Remove(item);
        _notify.ShowSuccess("Removed", $"'{item.Name}' will no longer run at startup.");
    }

    [RelayCommand]
    private async Task Delay(StartupItem? item)
    {
        if (item is null) return;
        // Apply a fixed, sensible 1-minute logon delay via a scheduled task.
        await _startup.SetDelayAsync(item, 60);
        _notify.ShowInfo("Delayed", $"'{item.Name}' will start ~1 minute after logon.");
    }
}
