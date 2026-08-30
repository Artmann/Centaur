namespace Centaur.App.Splits;

/// <summary>Walks the pane tree. The tree is a handful of nodes deep at most, so these stay
/// straightforward recursions rather than anything the tree has to maintain.</summary>
static class PaneNodes
{
    /// <summary>The split holding <paramref name="target"/>, or null when it is the root.</summary>
    public static SplitPane? Parent(PaneNode current, PaneNode target)
    {
        if (current is not SplitPane split)
        {
            return null;
        }
        if (split.First == target || split.Second == target)
        {
            return split;
        }
        return Parent(split.First, target) ?? Parent(split.Second, target);
    }

    /// <summary>The leaf showing <paramref name="terminal"/>, or null when it isn't in the tree.</summary>
    public static LeafPane? Find(PaneNode node, IPaneTerminal terminal) =>
        node switch
        {
            LeafPane leaf when leaf.Terminal == terminal => leaf,
            SplitPane split => Find(split.First, terminal) ?? Find(split.Second, terminal),
            _ => null,
        };

    /// <summary>The leftmost/topmost leaf under <paramref name="node"/>.</summary>
    public static LeafPane FirstLeaf(PaneNode node)
    {
        while (node is SplitPane split)
        {
            node = split.First;
        }
        return (LeafPane)node;
    }

    /// <summary>Closes every terminal under <paramref name="node"/>.</summary>
    public static void CloseAll(PaneNode node)
    {
        switch (node)
        {
            case LeafPane leaf:
                leaf.Terminal.Close();
                break;
            case SplitPane split:
                CloseAll(split.First);
                CloseAll(split.Second);
                break;
        }
    }
}
