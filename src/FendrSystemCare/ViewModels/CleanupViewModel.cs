using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FendrSystemCare.Models;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.Utilities;

namespace FendrSystemCare.ViewModels;

/// <summary>
/// Cleanup page. Lets the user scan cleanable locations, see how much can be
/// recovered per category and clean the selected ones. Scanning is read-only;
/// cleaning is cancellable and reports progress.
/// </summary>
public sealed partial class CleanupViewModel : ViewModelBase
{
    private readonly ICleanupService _cleanup;
    private readonly INotificationService _notify;
    private CancellationTokenSource? _cts;

    public ObservableCollection<CleanupCategory> Categories { get; } = new();

    [ObservableProperty] private long _totalRecoverable;
    [ObservableProperty] private long _lastFreed;
    [ObservableProperty] private bool _hasScanned;

    public CleanupViewModel(ICleanupService cleanup, INotificationService notify)
    {
        Title = "Cleanup";
        _cleanup = cleanup;
        _notify = notify;

        foreach (var category in _cleanup.GetCategories())
        {
            category.PropertyChanged += OnCategoryChanged;
            Categories.Add(category);
        }
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task Scan()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        StatusMessage = "Scanning...";
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        try
        {
            await _cleanup.ScanAsync(Categories, _cts.Token);
            HasScanned = true;
            RecalculateTotal();
            _notify.ShowInfo("Scan complete", $"{FormatHelper.Bytes(TotalRecoverable)} can be recovered.");
        }
        catch (OperationCanceledException) { StatusMessage = "Scan cancelled."; }
        finally { Finish(); }
    }

    [RelayCommand(CanExecute = nameof(CanClean))]
    private async Task Clean()
    {
        _cts = new CancellationTokenSource();
        IsBusy = true;
        var progress = new Progress<string>(m => StatusMessage = m);
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
        try
        {
            LastFreed = await _cleanup.CleanAsync(Categories, progress, _cts.Token);
            RecalculateTotal();
            _notify.ShowSuccess("Cleanup complete", $"Recovered {FormatHelper.Bytes(LastFreed)}.");
        }
        catch (OperationCanceledException) { StatusMessage = "Cleanup cancelled."; }
        finally { Finish(); }
    }

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SelectAll() => SetAllSelected(true);

    [RelayCommand]
    private void SelectNone() => SetAllSelected(false);

    private bool CanStart() => !IsBusy;
    private bool CanClean() => !IsBusy && HasScanned && Categories.Any(c => c.IsSelected && c.SizeBytes > 0);

    private void SetAllSelected(bool value)
    {
        foreach (var c in Categories) c.IsSelected = value;
    }

    private void OnCategoryChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(CleanupCategory.IsSelected) or nameof(CleanupCategory.SizeBytes))
        {
            RecalculateTotal();
            CleanCommand.NotifyCanExecuteChanged();
        }
    }

    private void RecalculateTotal() =>
        TotalRecoverable = Categories.Where(c => c.IsSelected).Sum(c => c.SizeBytes);

    private void Finish()
    {
        IsBusy = false;
        _cts?.Dispose();
        _cts = null;
        ScanCommand.NotifyCanExecuteChanged();
        CleanCommand.NotifyCanExecuteChanged();
    }
}
