using Centaur.Core.Hosting;
using Xunit;

namespace Centaur.Tests;

public class NotificationServiceTests
{
    class TestNotificationService : INotificationService, IProvider
    {
        public List<(
            string Title,
            string Message,
            NotificationSeverity Severity
        )> Notifications { get; } = [];

        public void Show(
            string title,
            string message,
            NotificationSeverity severity = NotificationSeverity.Info
        )
        {
            Notifications.Add((title, message, severity));
        }
    }

    [Fact]
    public void Show_RecordsNotification()
    {
        var service = new TestNotificationService();
        service.Show("Test", "Hello", NotificationSeverity.Error);

        Assert.Single(service.Notifications);
        Assert.Equal("Test", service.Notifications[0].Title);
        Assert.Equal("Hello", service.Notifications[0].Message);
        Assert.Equal(NotificationSeverity.Error, service.Notifications[0].Severity);
    }

    [Fact]
    public void Show_DefaultSeverityIsInfo()
    {
        var service = new TestNotificationService();
        service.Show("Test", "Hello");

        Assert.Equal(NotificationSeverity.Info, service.Notifications[0].Severity);
    }

    [Fact]
    public async Task NotificationService_ResolvableViaExtensionHost()
    {
        var service = new TestNotificationService();
        var host = new ExtensionHost();
        host.RegisterProvider(service);

        await host.ActivateAsync();

        var resolved = host.GetProvider<INotificationService>();
        Assert.NotNull(resolved);
        Assert.Same(service, resolved);
    }
}
