using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Microsoft.Extensions.DependencyInjection;

namespace Centaur.App;

public partial class MainWindow : Window
{
    const int horizontalPadding = 8;
    const int bottomPadding = 8;
    const int titleBarHeight = 28;

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

        UpdateTerminalMargin();
        PropertyChanged += (_, e) =>
        {
            if (
                e.Property == WindowDecorationMarginProperty
                || e.Property == OffScreenMarginProperty
            )
            {
                UpdateTerminalMargin();
            }
        };

        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    WindowState =
                        WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                }
                else
                {
                    BeginMoveDrag(e);
                }
            }
        };

        minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        maximizeButton.Click += (_, _) =>
        {
            WindowState =
                WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        };
        closeButton.Click += (_, _) => Close();

        Loaded += (_, _) => terminal.Focus();
    }

    void UpdateTerminalMargin()
    {
        var top = WindowDecorationMargin.Top > 0 ? WindowDecorationMargin.Top : titleBarHeight;
        var offScreen = OffScreenMargin;
        titleBarPanel.Margin = new Thickness(offScreen.Left, offScreen.Top, offScreen.Right, 0);
        terminal.Margin = new Thickness(
            horizontalPadding + offScreen.Left,
            top + offScreen.Top,
            horizontalPadding + offScreen.Right,
            bottomPadding + offScreen.Bottom
        );
    }
}
