using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using FendrSystemCare.Models;
using FendrSystemCare.Services;
using FendrSystemCare.Services.Interfaces;
using FendrSystemCare.ViewModels;
using Hardcodet.Wpf.TaskbarNotification;
using Wpf.Ui.Controls;

namespace FendrSystemCare;

/// <summary>
/// Main shell window. Hosts the sidebar + content area, surfaces notifications
/// as an auto-dismissing InfoBar plus a tray balloon, and supports minimising
/// to the system tray for background monitoring.
/// </summary>
public partial class MainWindow
{
    private readonly MainViewModel _viewModel;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _notificationTimer;

    public MainWindow(MainViewModel viewModel, INotificationService notification, ISettingsService settings)
    {
        InitializeComponent();

        _viewModel = viewModel;
        _settings = settings;
        DataContext = _viewModel;

        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _notificationTimer.Tick += (_, _) =>
        {
            Notification.IsOpen = false;
            _notificationTimer.Stop();
        };

        // The concrete service exposes the notification event.
        if (notification is NotificationService concrete)
            concrete.NotificationRequested += OnNotificationRequested;

        Loaded += (_, _) => _viewModel.Initialize();
        StateChanged += OnStateChanged;
        PreviewKeyDown += OnPreviewKeyDown;
    }

    // Ctrl+F focuses the search box from anywhere in the window.
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            SearchBox.Focus();
            e.Handled = true;
        }
    }

    // Animates the sidebar between expanded (labels) and collapsed (icons only).
    private void OnToggleSidebar(object sender, RoutedEventArgs e)
    {
        var expand = !_viewModel.IsSidebarExpanded;
        _viewModel.IsSidebarExpanded = expand;

        var animation = new DoubleAnimation(expand ? 248 : 68, TimeSpan.FromSeconds(0.22))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
        };
        Sidebar.BeginAnimation(WidthProperty, animation);
    }

    private void OnNotificationRequested(object? sender, NotificationEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (!_settings.Current.NotificationsEnabled) return;

            Notification.Title = e.Title;
            Notification.Message = e.Message;
            Notification.Severity = e.Level switch
            {
                LogLevel.Success => InfoBarSeverity.Success,
                LogLevel.Warning => InfoBarSeverity.Warning,
                LogLevel.Error => InfoBarSeverity.Error,
                _ => InfoBarSeverity.Informational
            };
            Notification.IsOpen = true;

            _notificationTimer.Stop();
            _notificationTimer.Start();

            // Also raise a tray balloon so notifications are seen when minimised.
            var icon = e.Level switch
            {
                LogLevel.Success => BalloonIcon.Info,
                LogLevel.Warning => BalloonIcon.Warning,
                LogLevel.Error => BalloonIcon.Error,
                _ => BalloonIcon.Info
            };
            TrayIcon.ShowBalloonTip(e.Title, e.Message, icon);
        });
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _settings.Current.MinimizeToTray)
            Hide();
    }

    private void OnTrayOpen(object sender, RoutedEventArgs e)
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnTrayExit(object sender, RoutedEventArgs e)
    {
        TrayIcon.Dispose();
        Application.Current.Shutdown();
    }
}
