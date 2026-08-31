using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

public partial class MainWindow : Window
{
    const int topPadding = 4;
    const int titleBarHeight = 36;

    readonly ExtensionHost host;
    readonly TabManager tabManager;
    readonly SessionManager sessionManager;
    readonly TerminalServices services;

    // The window's own background, rather than the shared chromeBase brush, because window
    // opacity gives this one an alpha and every other user of that brush wants it opaque.
    readonly SolidColorBrush windowBackground = new();

    SettingsPage? settingsPage;

    public MainWindow(
        TerminalServices services,
        NotificationServiceExtension notificationService,
        SessionStore sessions
    )
    {
        InitializeComponent();

        this.services = services;
        host = services.Host;
        AttachNotifications(notificationService);

        tabManager = new TabManager(contentPanel, Close, services);
        sessionManager = new SessionManager(this, tabManager, sessions, services.Notifications);

        Background = windowBackground;

        WireTabBar();
        WireContentMargin();
        WireTitleBar();
        WireLifecycle();
        WireSettings();

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

    /// <summary>
    /// The window furniture follows the theme, and the terminal area follows the padding and
    /// opacity settings, so the whole window changes together rather than a pane at a time.
    /// </summary>
    void WireSettings()
    {
        UpdateWindowOpacity();

        services.Settings.Changed += id =>
        {
            if (!SettingIds.AffectsWindow(id))
            {
                return;
            }

            ChromeTheme.Apply(services.Theme);
            UpdateContentMargin();
            UpdateWindowOpacity();
        };
    }

    void ToggleSettings()
    {
        settingsPage ??= CreateSettingsPage();

        if (settingsPage.IsOpen)
        {
            CloseSettings();
            return;
        }

        host.Events.Publish(new SettingsRequestedEvent());
        settingsPage.Show();
    }

    SettingsPage CreateSettingsPage()
    {
        var page = new SettingsPage(services);
        page.CloseRequested += CloseSettings;
        settingsHost.Children.Add(page);
        return page;
    }

    void CloseSettings()
    {
        settingsPage?.Hide();

        // The page took the keyboard when it opened, so it has to give it back - otherwise the
        // terminal behind it silently ignores everything typed next.
        tabManager.FocusActivePane();
    }

    void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        // Escape belongs to the page while it is open; the panes behind it never see it.
        if (settingsPage is { IsOpen: true } && e.Key == Key.Escape)
        {
            CloseSettings();
            e.Handled = true;
            return;
        }

        if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            return;
        }

        if (e.Key == Key.OemComma)
        {
            ToggleSettings();
            e.Handled = true;
            return;
        }

        // A stray Ctrl+W behind an open settings page would close a pane the user cannot see.
        // The page swallows the tab shortcuts rather than acting on them.
        if (settingsPage is { IsOpen: true })
        {
            e.Handled = e.Key is Key.T or Key.Tab or Key.W or (>= Key.D1 and <= Key.D9);
            return;
        }

        HandleTabShortcut(e);
    }

    /// <summary>The Ctrl shortcuts that act on tabs and panes, once the settings page has had
    /// its say.</summary>
    void HandleTabShortcut(KeyEventArgs e)
    {
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
        var padding = services.Settings.ContentPadding;

        titleBarPanel.Margin = new Thickness(offScreen.Left, offScreen.Top, offScreen.Right, 0);

        var content = new Thickness(
            padding + offScreen.Left,
            top + topPadding + offScreen.Top,
            padding + offScreen.Right,
            padding + offScreen.Bottom
        );

        contentPanel.Margin = content;

        // The page covers the terminal area exactly, leaving the tab strip and the caption
        // buttons live above it.
        settingsHost.Margin = content;
    }

    /// <summary>
    /// A see-through window needs three things to agree: the transparency hint, this brush's
    /// alpha, and the alpha the renderer clears each pane with (<c>TerminalAppearance</c>).
    /// Miss one and the window is opaque with no sign of why.
    /// </summary>
    void UpdateWindowOpacity()
    {
        var opacity = services.Settings.WindowOpacity;
        var color = ChromeTheme.Base.Color;

        windowBackground.Color =
            opacity >= 1.0
                ? Color.FromRgb(color.R, color.G, color.B)
                : Color.FromArgb(
                    (byte)Math.Clamp(opacity * 255, 0, 255),
                    color.R,
                    color.G,
                    color.B
                );

        TransparencyLevelHint =
            opacity >= 1.0 ? [WindowTransparencyLevel.None] : [WindowTransparencyLevel.Transparent];
    }
}
