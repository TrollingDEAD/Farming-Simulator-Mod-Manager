using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;

namespace FsModManager.App.Services;

public enum NotificationKind
{
    Info,
    Warning,
    Error,
}

public sealed class AppNotification
{
    public AppNotification(string message, NotificationKind kind)
    {
        Message = message;
        Kind = kind;
    }

    public string Message { get; }

    public NotificationKind Kind { get; }
}

/// <summary>Collects user-facing messages shown as dismissible banners in the shell.</summary>
public interface INotificationService
{
    ObservableCollection<AppNotification> Notifications { get; }

    void Info(string message);

    void Warning(string message);

    void Error(string message);

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

    public void Info(string message) => Show(new AppNotification(message, NotificationKind.Info));

    public void Warning(string message) => Show(new AppNotification(message, NotificationKind.Warning));

    public void Error(string message) => Show(new AppNotification(message, NotificationKind.Error));

    public void Dismiss(AppNotification notification) => RunOnUi(() => Notifications.Remove(notification));

    private void Show(AppNotification notification)
    {
        RunOnUi(() =>
        {
            while (Notifications.Count >= MaxVisible)
            {
                Notifications.RemoveAt(0);
            }

            Notifications.Add(notification);

            if (notification.Kind != NotificationKind.Error)
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
    }

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
