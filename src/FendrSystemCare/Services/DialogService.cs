using System.Windows;
using FendrSystemCare.Services.Interfaces;
using Microsoft.Win32;

namespace FendrSystemCare.Services;

/// <summary>Shows confirmation dialogs and folder pickers on the UI thread.</summary>
public interface IDialogService
{
    /// <summary>Asks the user to confirm a potentially destructive action.</summary>
    bool Confirm(string title, string message);

    /// <summary>Prompts for a destination folder; returns null when cancelled.</summary>
    string? PickFolder(string title);
}

/// <inheritdoc />
public sealed class DialogService : IDialogService
{
    public bool Confirm(string title, string message) =>
        Application.Current.Dispatcher.Invoke(() =>
            MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes);

    public string? PickFolder(string title)
    {
        return Application.Current.Dispatcher.Invoke(() =>
        {
            var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
            return dialog.ShowDialog() == true ? dialog.FolderName : null;
        });
    }
}
