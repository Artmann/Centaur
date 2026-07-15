using Avalonia.Layout;
using Centaur.App.Splits;

namespace Centaur.App;

public static class SessionSnapshot
{
    public static SessionNode Capture(PaneNode node) =>
        node switch
        {
            LeafPane leaf => new SessionNode
            {
                IsSplit = false,
                WorkingDirectory = leaf.Terminal.WorkingDirectory,
            },
            SplitPane split => new SessionNode
            {
                IsSplit = true,
                Orientation = split.Orientation,
                Ratio = split.ComputeRatio(),
                First = Capture(split.First),
                Second = Capture(split.Second),
            },
            _ => throw new ArgumentOutOfRangeException(nameof(node)),
        };

    // `leaf` must already be seeded with FirstLeafWorkingDirectory(node) — true at the top
    // level by construction (PaneTree's initial leaf), and preserved by this method for the
    // new leaf it creates before recursing into it.
    public static void RestoreInto(SessionNode node, LeafPane leaf, PaneTree tree)
    {
        if (!node.IsSplit)
        {
            return;
        }

        if (node.First == null || node.Second == null)
        {
            throw new InvalidOperationException(
                "Malformed session node: a split node is missing its First/Second child."
            );
        }

        var direction =
            node.Orientation == Orientation.Horizontal ? SplitDirection.Right : SplitDirection.Down;
        var newLeaf = tree.Split(
            leaf,
            direction,
            FirstLeafWorkingDirectory(node.Second),
            node.Ratio
        );

        RestoreInto(node.First, leaf, tree);
        RestoreInto(node.Second, newLeaf, tree);
    }

    public static string? FirstLeafWorkingDirectory(SessionNode node)
    {
        while (node.IsSplit)
        {
            if (node.First == null)
            {
                throw new InvalidOperationException(
                    "Malformed session node: a split node is missing its First child."
                );
            }
            node = node.First;
        }
        return node.WorkingDirectory;
    }
}
