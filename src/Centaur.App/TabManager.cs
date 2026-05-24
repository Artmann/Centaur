using Avalonia.Controls;
using Centaur.App.Splits;

namespace Centaur.App;

public class TabManager
{
    readonly Panel contentPanel;
    readonly Action closeWindow;
    readonly List<TabItem> tabs = [];
    int nextId = 1;
    int activeTabId = -1;

    public IReadOnlyList<TabItem> Tabs => tabs;
    public int ActiveTabId => activeTabId;
    public event Action? TabsChanged;

    public TabManager(Panel contentPanel, Action closeWindow)
    {
        this.contentPanel = contentPanel;
        this.closeWindow = closeWindow;
    }

    public TabItem CreateTab()
    {
        TabItem? tab = null;
        var panes = new PaneTree(() =>
        {
            var terminal = new TerminalControl();
            terminal.SplitRequested += direction =>
            {
                if (tab == null)
                {
                    return;
                }
                var leaf = tab.Panes.LeafFor(terminal);
                if (leaf != null)
                {
                    tab.Panes.Split(leaf, direction);
                }
            };
            terminal.CloseRequested += () =>
            {
                if (tab == null)
                {
                    return;
                }
                var leaf = tab.Panes.LeafFor(terminal);
                if (leaf != null)
                {
                    ClosePane(tab, leaf);
                }
            };
            terminal.PtyExited += () =>
            {
                if (tab == null)
                {
                    return;
                }
                var leaf = tab.Panes.LeafFor(terminal);
                if (leaf != null)
                {
                    ClosePane(tab, leaf);
                }
            };
            return terminal;
        });

        tab = new TabItem
        {
            Id = nextId++,
            Title = $"Terminal {tabs.Count + 1}",
            Panes = panes,
        };

        panes.RootView.IsVisible = false;
        contentPanel.Children.Add(panes.RootView);

        tabs.Add(tab);
        ActivateTab(tab.Id);

        return tab;
    }

    public void ActivateTab(int id)
    {
        var target = tabs.Find(t => t.Id == id);
        if (target == null)
        {
            return;
        }

        foreach (var tab in tabs)
        {
            tab.Panes.RootView.IsVisible = tab.Id == id;
        }

        activeTabId = id;
        target.Panes.FocusedLeaf.Terminal.Focus();
        TabsChanged?.Invoke();
    }

    public void CloseTab(int id)
    {
        var index = tabs.FindIndex(t => t.Id == id);
        if (index < 0)
        {
            return;
        }

        var tab = tabs[index];
        tab.Panes.DisposeAll();
        tabs.RemoveAt(index);

        if (tabs.Count == 0)
        {
            contentPanel.Children.Remove(tab.Panes.RootView);
            closeWindow();
            return;
        }

        if (activeTabId == id)
        {
            var newIndex = Math.Min(index, tabs.Count - 1);
            ActivateTab(tabs[newIndex].Id);
        }

        contentPanel.Children.Remove(tab.Panes.RootView);
        TabsChanged?.Invoke();
    }

    public void ClosePane(TabItem tab, LeafPane leaf)
    {
        var treeEmpty = tab.Panes.Close(leaf);
        if (treeEmpty)
        {
            CloseTab(tab.Id);
        }
    }

    public void CloseFocusedPane()
    {
        var tab = tabs.Find(t => t.Id == activeTabId);
        if (tab == null)
        {
            return;
        }
        ClosePane(tab, tab.Panes.FocusedLeaf);
    }

    public void RenameTab(int id, string title)
    {
        var tab = tabs.Find(t => t.Id == id);
        if (tab == null)
        {
            return;
        }

        tab.Title = title;
        TabsChanged?.Invoke();
    }

    public void MoveTab(int id, int newIndex)
    {
        var oldIndex = tabs.FindIndex(t => t.Id == id);
        if (oldIndex < 0 || newIndex < 0 || newIndex >= tabs.Count || oldIndex == newIndex)
        {
            return;
        }

        var tab = tabs[oldIndex];
        tabs.RemoveAt(oldIndex);
        tabs.Insert(newIndex, tab);
        TabsChanged?.Invoke();
    }

    public void ActivateNextTab()
    {
        if (tabs.Count <= 1)
        {
            return;
        }

        var index = tabs.FindIndex(t => t.Id == activeTabId);
        var nextIndex = (index + 1) % tabs.Count;
        ActivateTab(tabs[nextIndex].Id);
    }

    public void ActivateTabByIndex(int index)
    {
        if (index >= 0 && index < tabs.Count)
        {
            ActivateTab(tabs[index].Id);
        }
    }
}
