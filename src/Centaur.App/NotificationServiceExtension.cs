using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using Centaur.Core.Hosting;

namespace Centaur.App;

public class NotificationServiceExtension : IExtension, INotificationService, IProvider
{
    WindowNotificationManager? manager;

    public int Priority => 1000;

    public void SetManager(WindowNotificationManager manager)
    {
        this.manager = manager;
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
        if (manager == null)
        {
            return;
        }

        var type = severity switch
        {
            NotificationSeverity.Success => NotificationType.Success,
            NotificationSeverity.Warning => NotificationType.Warning,
            NotificationSeverity.Error => NotificationType.Error,
            _ => NotificationType.Information,
        };

        Dispatcher.UIThread.Post(() =>
        {
            manager.Show(new Notification(title, message, type));
        });
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        manager = null;
        return ValueTask.CompletedTask;
    }
}
