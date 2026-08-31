using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Centaur.Core.Hosting;

namespace Centaur.App;

public partial class MainWindow : Window
{
    const int horizontalPadding = 8;
    const int topPadding = 4;
    const int bottomPadding = 8;
    const int titleBarHeight = 36;

    readonly ExtensionHost host;
    readonly TabManager tabManager;
    readonly SessionManager sessionManager;

    public MainWindow(
        TerminalServices services,
        NotificationServiceExtension notificationService,
        SessionStore sessions
    )
    {
        InitializeComponent();

        host = services.Host;
        AttachNotifications(notificationService);

        tabManager = new TabManager(contentPanel, Close, services);
        sessionManager = new SessionManager(this, tabManager, sessions, services.Notifications);

        WireTabBar();
        WireContentMargin();
        WireTitleBar();
        WireLifecycle();

        // Intercept tab shortcuts before they reach TerminalControl
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    /// <summary>The notification manager needs a window, so the service cannot be handed one
    /// until now; anything reported before this point is queued and flushed here.</summary>
    void AttachNotifications(NotificationServiceExtension notificationService) =>
        notificationService.SetManager(
            new WindowNotificationManager(this)
            {
                Position = NotificationPosition.BottomRight,
                MaxItems = 3,
            }
        );

    void WireTabBar()
    {
        tabBar.TabSelected += id => tabManager.ActivateTab(id);
        tabBar.NewTabRequested += () => tabManager.CreateTab();
        tabBar.TabClosed += id => tabManager.CloseTab(id);
        tabBar.TabRenamed += (id, title) => tabManager.RenameTab(id, title);
        tabBar.TabMoved += (id, newIndex) => tabManager.MoveTab(id, newIndex);
        tabManager.TabsChanged += () => tabBar.Update(tabManager.Tabs, tabManager.ActiveTabId);
    }

    /// <summary>Keeps the content clear of the title bar and of whatever the window manager
    /// takes for decorations, which changes as the window is maximized or moved.</summary>
    void WireContentMargin()
    {
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
    }

    /// <summary>The custom title bar: drag to move, double-click and the three buttons.</summary>
    void WireTitleBar()
    {
        titleBar.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            {
                if (e.ClickCount == 2)
                {
                    ToggleMaximized();
                }
                else
                {
                    BeginMoveDrag(e);
                }
            }
        };

        minimizeButton.Click += (_, _) => WindowState = WindowState.Minimized;
        maximizeButton.Click += (_, _) => ToggleMaximized();
        closeButton.Click += (_, _) => Close();
    }

    void ToggleMaximized() =>
        WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    void WireLifecycle()
    {
        Loaded += async (_, _) =>
        {
            await host.ActivateAsync();
            sessionManager.RestoreTabsOrCreateInitial();
        };

        Closed += async (_, _) =>
        {
            sessionManager.FlushPendingSave();
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
                tabManager.CloseFocusedPane();
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
            top + topPadding + offScreen.Top,
            horizontalPadding + offScreen.Right,
            bottomPadding + offScreen.Bottom
        );
    }
}
