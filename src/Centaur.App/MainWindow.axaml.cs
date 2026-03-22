using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Centaur.Core.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Centaur.App;

public partial class MainWindow : Window
{
    const int horizontalPadding = 8;
    const int bottomPadding = 8;
    const int titleBarHeight = 28;

    readonly TabManager tabManager;

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

        tabManager = new TabManager(contentPanel, Close);

        tabBar.TabSelected += id => tabManager.ActivateTab(id);
        tabBar.NewTabRequested += () => tabManager.CreateTab();
        tabBar.TabClosed += id => tabManager.CloseTab(id);
        tabManager.TabsChanged += () => tabBar.Update(tabManager.Tabs, tabManager.ActiveTabId);

        UpdateContentMargin();
        PropertyChanged += (_, e) =>
        {
            if (
                e.Property == WindowDecorationMarginProperty
                || e.Property == OffScreenMarginProperty
            )
            {
                UpdateContentMargin();
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

        // Intercept tab shortcuts before they reach TerminalControl
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);

        Loaded += async (_, _) =>
        {
            var host = App.Services.GetRequiredService<ExtensionHost>();
            await host.ActivateAsync();
            tabManager.CreateTab();
        };

        Closed += async (_, _) =>
        {
            var host = App.Services.GetRequiredService<ExtensionHost>();
            await host.DisposeAsync();
        };
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        switch (e.Key)
        {
            case Key.T:
                tabManager.CreateTab();
                e.Handled = true;
                break;
            case Key.Tab:
                tabManager.ActivateNextTab();
                e.Handled = true;
                break;
            case Key.W:
                tabManager.CloseTab(tabManager.ActiveTabId);
                e.Handled = true;
                break;
            case >= Key.D1 and <= Key.D9:
                tabManager.ActivateTabByIndex(e.Key - Key.D1);
                e.Handled = true;
                break;
        }
    }

    void UpdateContentMargin()
    {
        var top = WindowDecorationMargin.Top > 0 ? WindowDecorationMargin.Top : titleBarHeight;
        var offScreen = OffScreenMargin;
        titleBarPanel.Margin = new Thickness(offScreen.Left, offScreen.Top, offScreen.Right, 0);
        contentPanel.Margin = new Thickness(
            horizontalPadding + offScreen.Left,
            top + offScreen.Top,
            horizontalPadding + offScreen.Right,
            bottomPadding + offScreen.Bottom
        );
    }
}
