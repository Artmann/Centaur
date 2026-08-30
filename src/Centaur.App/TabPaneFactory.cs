using Centaur.App.Splits;

namespace Centaur.App;

/// <summary>Builds the terminals for one tab's panes and wires each one back to the tab it
/// lives in. The tab does not exist yet when its first pane is created — a PaneTree asks for
/// that pane from its own constructor — so <see cref="Owner"/> is assigned straight after.</summary>
sealed class TabPaneFactory
{
    readonly TerminalServices services;
    readonly Action<TabItem, LeafPane> closePane;

    public TabPaneFactory(TerminalServices services, Action<TabItem, LeafPane> closePane)
    {
        this.services = services;
        this.closePane = closePane;
    }

    public TabItem? Owner { get; set; }

    public IPaneTerminal Create(string? workingDirectory)
    {
        var terminal = new TerminalControl(services, workingDirectory);

        terminal.SplitRequested += direction =>
            WithLeaf(terminal, (tab, leaf) => tab.Panes.Split(leaf, direction));
        terminal.CloseRequested += () => WithLeaf(terminal, closePane);
        terminal.PtyExited += () => WithLeaf(terminal, closePane);

        return terminal;
    }

    /// <summary>Runs <paramref name="action"/> against the pane this terminal sits in, if the
    /// tab is still around and still holds it — a pane that has already been closed has neither.</summary>
    void WithLeaf(IPaneTerminal terminal, Action<TabItem, LeafPane> action)
    {
        var tab = Owner;
        var leaf = tab?.Panes.LeafFor(terminal);
        if (tab != null && leaf != null)
        {
            action(tab, leaf);
        }
    }
}
