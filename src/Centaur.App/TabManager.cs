using Avalonia.Controls;

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
        var terminal = new TerminalControl();
        var tab = new TabItem
        {
            Id = nextId++,
            Title = $"Terminal {tabs.Count + 1}",
            Terminal = terminal,
        };

        terminal.IsVisible = false;
        contentPanel.Children.Add(terminal);

        terminal.PtyExited += () => CloseTab(tab.Id);

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
            tab.Terminal.IsVisible = tab.Id == id;
        }

        activeTabId = id;
        target.Terminal.Focus();
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
        tabs.RemoveAt(index);

        if (tabs.Count == 0)
        {
            contentPanel.Children.Remove(tab.Terminal);
            closeWindow();
            return;
        }

        if (activeTabId == id)
        {
            var newIndex = Math.Min(index, tabs.Count - 1);
            ActivateTab(tabs[newIndex].Id);
        }

        contentPanel.Children.Remove(tab.Terminal);
        TabsChanged?.Invoke();
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
