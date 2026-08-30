using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Centaur.Core.Hosting;

namespace Centaur.App;

public class NotificationServiceExtension : IExtension, INotificationService, IProvider
{
    WindowNotificationManager? manager;

    // Notifications raised before the main window exists (settings/session load errors, for
    // example) are held here and flushed once the manager attaches, so they are never lost.
    readonly List<Notification> pending = [];

    public int Priority => 1000;

    public void SetManager(WindowNotificationManager manager)
    {
        this.manager = manager;

        foreach (var notification in pending)
        {
            Show(notification);
        }

        pending.Clear();
    }

    public Task ActivateAsync(IExtensionContext context)
    {
        return Task.CompletedTask;
    }

    public void Show(
        string title,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info
    )
    {
        var type = severity switch
        {
            NotificationSeverity.Success => NotificationType.Success,
            NotificationSeverity.Warning => NotificationType.Warning,
            NotificationSeverity.Error => NotificationType.Error,
            _ => NotificationType.Information,
        };

        var notification = new Notification(title, message, type);

        if (manager == null)
        {
            pending.Add(notification);
            return;
        }

        Show(notification);
    }

    void Show(Notification notification)
    {
        var target = manager;
        if (target == null)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            target.Show(notification);
        });
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        manager = null;
        pending.Clear();
        return ValueTask.CompletedTask;
    }
}
