using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace FsModManager.App.Services;

public enum NotificationKind
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// One banner in the shell. The message is mutable so a caller can update a visible banner
/// in place (e.g. download progress). A banner can carry one action button
/// (<see cref="ActionLabel"/>/<see cref="ActionCommand"/>) — banners with an action never
/// auto-dismiss, so a choice is never silently taken away from the user.
/// </summary>
public sealed partial class AppNotification : ObservableObject
{
    public AppNotification(string message, NotificationKind kind, string? actionLabel = null, Action? action = null)
    {
        _message = message;
        Kind = kind;
        ActionLabel = actionLabel;
        ActionCommand = action is null ? null : new RelayCommand(action);
    }

    [ObservableProperty]
    private string _message;

    public NotificationKind Kind { get; }

    public string? ActionLabel { get; }

    public ICommand? ActionCommand { get; }

    public bool HasAction => ActionCommand is not null;
}

/// <summary>Collects user-facing messages shown as dismissible banners in the shell.</summary>
public interface INotificationService
{
    ObservableCollection<AppNotification> Notifications { get; }

    void Info(string message);

    void Warning(string message);

    void Error(string message);

    /// <summary>
    /// Shows a banner and returns it, so the caller can update
    /// <see cref="AppNotification.Message"/> in place later (e.g. progress). When
    /// <paramref name="actionLabel"/>/<paramref name="action"/> are supplied the banner shows an
    /// action button and does NOT auto-dismiss.
    /// </summary>
    AppNotification Show(string message, NotificationKind kind, string? actionLabel = null, Action? action = null);

    void Dismiss(AppNotification notification);
}

/// <summary>
/// Banner-style notifications. Info/warning messages auto-dismiss; errors stay until dismissed.
/// Marshals to the UI dispatcher when raised from a background continuation.
/// </summary>
public sealed class NotificationService : INotificationService
{
    private const int MaxVisible = 3;
    private static readonly TimeSpan AutoDismissDelay = TimeSpan.FromSeconds(8);

    public ObservableCollection<AppNotification> Notifications { get; } = new();

    public void Info(string message) => Show(message, NotificationKind.Info);

    public void Warning(string message) => Show(message, NotificationKind.Warning);

    public void Error(string message) => Show(message, NotificationKind.Error);

    public AppNotification Show(string message, NotificationKind kind, string? actionLabel = null, Action? action = null)
    {
        var notification = new AppNotification(message, kind, actionLabel, action);
        RunOnUi(() =>
        {
            while (Notifications.Count >= MaxVisible)
            {
                Notifications.RemoveAt(0);
            }

            Notifications.Add(notification);

            // Errors and actionable banners stay until explicitly dismissed/acted on —
            // auto-dismissing an action would silently take the choice away from the user.
            if (notification.Kind != NotificationKind.Error && !notification.HasAction)
            {
                var timer = new DispatcherTimer { Interval = AutoDismissDelay };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    Notifications.Remove(notification);
                };
                timer.Start();
            }
        });
        return notification;
    }

    public void Dismiss(AppNotification notification) => RunOnUi(() => Notifications.Remove(notification));

    private static void RunOnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.Invoke(action);
        }
    }
}
