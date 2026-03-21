using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Microsoft.Extensions.DependencyInjection;

namespace Centaur.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        var notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.BottomRight,
            MaxItems = 3,
        };

        var notificationService = App.Services.GetRequiredService<NotificationServiceExtension>();
        notificationService.SetManager(notificationManager);

        Loaded += (_, _) => terminal.Focus();
    }
}
