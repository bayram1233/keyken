using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// System Repair page. Presents the catalogue of repair tasks, lets the user
/// select which to run, creates a restore point up-front, then runs each task
/// sequentially while streaming console output. The whole run is cancellable.
/// </summary>
public sealed partial class SystemRepairViewModel : ViewModelBase
{
    private readonly IRepairService _repair;
    private readonly IRestorePointService _restore;
    private readonly IDialogService _dialog;
    private readonly INotificationService _notify;
    private readonly ISettingsService _settings;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RepairTask> Tasks { get; } = new();

    /// <summary>Live console output for the currently running task.</summary>
    public ObservableCollection<string> Output { get; } = new();

    public SystemRepairViewModel(IRepairService repair, IRestorePointService restore,
        IDialogService dialog, INotificationService notify, ISettingsService settings)
    {
        Title = "System Repair";
        _repair = repair;
        _restore = restore;
        _dialog = dialog;
        _notify = notify;
        _settings = settings;

        foreach (var task in _repair.GetTasks())
            Tasks.Add(task);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunSelected()
    {
        var selected = Tasks.Where(t => t.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _notify.ShowWarning("Nothing selected", "Select at least one repair task to run.");
            return;
        }

        var changesSystem = selected.Any(t => t.RequiresConfirmation);
        if (changesSystem && !_dialog.Confirm("Confirm repair",
                $"{selected.Count} task(s) will run and may modify your system. A restore point will be created first. Continue?"))
            return;

        _cts = new CancellationTokenSource();
        IsBusy = true;
        RunSelectedCommand.NotifyCanExecuteChanged();
        Output.Clear();

        try
        {
            if (changesSystem && _settings.Current.CreateRestorePointAutomatically)
            {
                StatusMessage = "Creating restore point...";
                Append("Creating system restore point...");
                await _restore.CreateAsync("Fendr System Care - before repair", _cts.Token);
            }

            var progress = new Progress<string>(Append);
            foreach (var task in selected)
            {
                _cts.Token.ThrowIfCancellationRequested();
                StatusMessage = $"Running: {task.Name}";
                Append($"\n=== {task.Name} ===");
                await _repair.RunAsync(task, progress, _cts.Token);
            }

            _notify.ShowSuccess("Repair complete", $"Finished {selected.Count} task(s).");
        }
        catch (OperationCanceledException)
        {
            Append("\nOperation cancelled by user.");
            StatusMessage = "Cancelled.";
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
            RunSelectedCommand.NotifyCanExecuteChanged();
        }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectRecommended()
    {
        // A sensible default set that diagnoses/repairs the most common issues.
        var recommended = new[] { "sfc", "dism_restore", "flush_dns" };
        foreach (var task in Tasks)
            task.IsSelected = recommended.Contains(task.Key);
    }

    private bool CanRun() => !IsBusy;

    private void Append(string line) =>
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Output.Add(line);
            while (Output.Count > 1000) Output.RemoveAt(0);
        });
}
