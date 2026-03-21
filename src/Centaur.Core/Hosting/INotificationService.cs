namespace Centaur.Core.Hosting;

public enum NotificationSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

public interface INotificationService
{
    void Show(
        string title,
        string message,
        NotificationSeverity severity = NotificationSeverity.Info
    );
}
