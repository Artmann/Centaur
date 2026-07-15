using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Centaur.Core.Hosting;

namespace Centaur.App;

// Owned directly by MainWindow, mirroring how TabManager itself sits outside the
// DI/extension system. Debounces window-bounds/layout changes into a single autosave so a
// crash still restores near-current state, not just a clean close.
public class SessionManager
{
    static readonly TimeSpan saveDebounce = TimeSpan.FromMilliseconds(400);

    readonly Window window;
    readonly TabManager tabManager;
    readonly SessionStore store;
    readonly INotificationService notifications;
    readonly DispatcherTimer saveTimer;
    bool isRestoring = true;
    bool hasPendingSave;

    public SessionManager(
        Window window,
        TabManager tabManager,
        SessionStore store,
        INotificationService notifications
    )
    {
        this.window = window;
        this.tabManager = tabManager;
        this.store = store;
        this.notifications = notifications;

        saveTimer = new DispatcherTimer { Interval = saveDebounce };
        saveTimer.Tick += (_, _) =>
        {
            saveTimer.Stop();
            hasPendingSave = false;
            SaveNow();
        };

        ApplyWindowBounds();

        window.Resized += (_, _) => ScheduleSave();
        window.PositionChanged += (_, _) => ScheduleSave();
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == Window.WindowStateProperty)
            {
                ScheduleSave();
            }
        };
        tabManager.LayoutChanged += ScheduleSave;
    }

    void ApplyWindowBounds()
    {
        var data = store.Data;
        window.Width = data.WindowWidth;
        window.Height = data.WindowHeight;

        if (data.WindowX is int x && data.WindowY is int y)
        {
            var point = new PixelPoint(x, y);
            var fitsOnScreen = false;
            foreach (var screen in window.Screens.All)
            {
                if (screen.Bounds.Contains(point))
                {
                    fitsOnScreen = true;
                    break;
                }
            }
            if (fitsOnScreen)
            {
                window.Position = point;
            }
        }

        if (data.WindowMaximized)
        {
            window.WindowState = WindowState.Maximized;
        }
    }

    // Called from MainWindow.Loaded, after the ExtensionHost has activated.
    public void RestoreTabsOrCreateInitial()
    {
        var data = store.Data;
        if (data.Tabs.Count == 0)
        {
            tabManager.CreateTab();
            isRestoring = false;
            return;
        }

        try
        {
            foreach (var sessionTab in data.Tabs)
            {
                var cwd = SessionSnapshot.FirstLeafWorkingDirectory(sessionTab.Root);
                var tab = tabManager.CreateTab(sessionTab.Title, cwd);
                SessionSnapshot.RestoreInto(sessionTab.Root, tab.Panes.FocusedLeaf, tab.Panes);
            }

            if (data.ActiveTabIndex >= 0 && data.ActiveTabIndex < tabManager.Tabs.Count)
            {
                tabManager.ActivateTab(tabManager.Tabs[data.ActiveTabIndex].Id);
            }
        }
        catch (Exception ex)
        {
            // Replace whatever partially restored — create the fallback tab first so
            // TabManager never sees a zero-tab state (which would close the window).
            var broken = tabManager.Tabs.ToArray();
            tabManager.CreateTab();
            foreach (var tab in broken)
            {
                tabManager.CloseTab(tab.Id);
            }

            notifications.Show(
                "Session Restore Error",
                $"Your saved tabs and panes couldn't be restored ({ex.Message}). Starting with a blank tab instead.",
                NotificationSeverity.Warning
            );
        }
        finally
        {
            isRestoring = false;
        }
    }

    void ScheduleSave()
    {
        if (isRestoring)
        {
            return;
        }
        hasPendingSave = true;
        saveTimer.Stop();
        saveTimer.Start();
    }

    void SaveNow()
    {
        try
        {
            store.Data = Capture();
            store.Save();
        }
        catch (Exception ex)
        {
            notifications.Show(
                "Session Save Failed",
                $"Could not save your current layout: {ex.Message}",
                NotificationSeverity.Warning
            );
        }
    }

    SessionData Capture()
    {
        var tabs = new List<SessionTab>();
        var activeIndex = 0;
        for (var i = 0; i < tabManager.Tabs.Count; i++)
        {
            var tab = tabManager.Tabs[i];
            if (tab.Id == tabManager.ActiveTabId)
            {
                activeIndex = i;
            }
            tabs.Add(
                new SessionTab { Title = tab.Title, Root = SessionSnapshot.Capture(tab.Panes.Root) }
            );
        }

        var isNormal = window.WindowState == WindowState.Normal;
        var previous = store.Data;
        return new SessionData
        {
            Tabs = tabs,
            ActiveTabIndex = activeIndex,
            WindowX = isNormal ? window.Position.X : previous.WindowX,
            WindowY = isNormal ? window.Position.Y : previous.WindowY,
            WindowWidth = isNormal ? window.Width : previous.WindowWidth,
            WindowHeight = isNormal ? window.Height : previous.WindowHeight,
            WindowMaximized = window.WindowState == WindowState.Maximized,
        };
    }

    // Called from MainWindow.Closed so a debounced save isn't lost on a clean close.
    public void FlushPendingSave()
    {
        if (!hasPendingSave)
        {
            return;
        }
        saveTimer.Stop();
        hasPendingSave = false;
        SaveNow();
    }
}
