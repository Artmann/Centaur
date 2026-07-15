using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace Centaur.App.Splits;

public enum SplitDirection
{
    Right,
    Left,
    Down,
    Up,
}

public abstract class PaneNode
{
    public Control View { get; protected set; } = null!;
}

public sealed class LeafPane : PaneNode
{
    public IPaneTerminal Terminal { get; }

    public LeafPane(IPaneTerminal terminal)
    {
        Terminal = terminal;
        View = terminal.View;
    }
}

public sealed class SplitPane : PaneNode
{
    public Orientation Orientation { get; }
    public PaneNode First { get; set; }
    public PaneNode Second { get; set; }
    public Grid GridView { get; }

    public event Action? RatioChanged;

    const double gutterThickness = 10;
    const double dividerThickness = 1;
    const double minRatio = 0.05;
    const double maxRatio = 0.95;
    static readonly IBrush gutterBrush = new SolidColorBrush(Color.FromRgb(0x24, 0x27, 0x3A));
    static readonly IBrush dividerBrush = new SolidColorBrush(Color.FromRgb(0x1B, 0x1D, 0x2A));

    public SplitPane(Orientation orientation, PaneNode first, PaneNode second, double ratio = 0.5)
    {
        Orientation = orientation;
        First = first;
        Second = second;
        GridView = new Grid();
        var clampedRatio = Math.Clamp(ratio, minRatio, maxRatio);

        if (orientation == Orientation.Horizontal)
        {
            GridView.ColumnDefinitions.Add(new ColumnDefinition(clampedRatio, GridUnitType.Star));
            GridView.ColumnDefinitions.Add(
                new ColumnDefinition(gutterThickness, GridUnitType.Pixel)
            );
            GridView.ColumnDefinitions.Add(
                new ColumnDefinition(1 - clampedRatio, GridUnitType.Star)
            );

            Grid.SetColumn(first.View, 0);
            Grid.SetColumn(second.View, 2);

            var splitter = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Columns,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = gutterBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetColumn(splitter, 1);
            splitter.DragCompleted += (_, _) => RatioChanged?.Invoke();

            var divider = new Border
            {
                Background = dividerBrush,
                Width = dividerThickness,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            };
            Grid.SetColumn(divider, 1);

            GridView.Children.Add(first.View);
            GridView.Children.Add(splitter);
            GridView.Children.Add(divider);
            GridView.Children.Add(second.View);
        }
        else
        {
            GridView.RowDefinitions.Add(new RowDefinition(clampedRatio, GridUnitType.Star));
            GridView.RowDefinitions.Add(new RowDefinition(gutterThickness, GridUnitType.Pixel));
            GridView.RowDefinitions.Add(new RowDefinition(1 - clampedRatio, GridUnitType.Star));

            Grid.SetRow(first.View, 0);
            Grid.SetRow(second.View, 2);

            var splitter = new GridSplitter
            {
                ResizeDirection = GridResizeDirection.Rows,
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                Background = gutterBrush,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            Grid.SetRow(splitter, 1);
            splitter.DragCompleted += (_, _) => RatioChanged?.Invoke();

            var divider = new Border
            {
                Background = dividerBrush,
                Height = dividerThickness,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };
            Grid.SetRow(divider, 1);

            GridView.Children.Add(first.View);
            GridView.Children.Add(splitter);
            GridView.Children.Add(divider);
            GridView.Children.Add(second.View);
        }

        View = GridView;
    }

    public double ComputeRatio()
    {
        var (firstWeight, secondWeight) =
            Orientation == Orientation.Horizontal
                ? (
                    GridView.ColumnDefinitions[0].Width.Value,
                    GridView.ColumnDefinitions[2].Width.Value
                )
                : (
                    GridView.RowDefinitions[0].Height.Value,
                    GridView.RowDefinitions[2].Height.Value
                );
        var total = firstWeight + secondWeight;
        return total <= 0 ? 0.5 : Math.Clamp(firstWeight / total, minRatio, maxRatio);
    }

    public void PlaceChild(PaneNode child, int cellIndex)
    {
        if (Orientation == Orientation.Horizontal)
        {
            Grid.SetColumn(child.View, cellIndex);
        }
        else
        {
            Grid.SetRow(child.View, cellIndex);
        }

        if (!GridView.Children.Contains(child.View))
        {
            GridView.Children.Add(child.View);
        }
    }
}

public sealed class PaneTree
{
    readonly Func<string?, IPaneTerminal> terminalFactory;
    PaneNode root;
    LeafPane focusedLeaf;

    public Panel RootView { get; } = new();
    public PaneNode Root => root;
    public LeafPane FocusedLeaf => focusedLeaf;

    public event Action? FocusedLeafChanged;
    public event Action? LayoutChanged;

    public PaneTree(
        Func<string?, IPaneTerminal> terminalFactory,
        string? initialWorkingDirectory = null
    )
    {
        this.terminalFactory = terminalFactory;

        var terminal = terminalFactory(initialWorkingDirectory);
        var leaf = new LeafPane(terminal);
        TrackLeaf(leaf);

        root = leaf;
        focusedLeaf = leaf;
        RootView.Children.Add(root.View);
    }

    public LeafPane? LeafFor(IPaneTerminal terminal)
    {
        return FindLeaf(root, terminal);
    }

    public LeafPane Split(
        LeafPane target,
        SplitDirection direction,
        string? workingDirectory = null,
        double ratio = 0.5
    )
    {
        var orientation = direction is SplitDirection.Right or SplitDirection.Left
            ? Orientation.Horizontal
            : Orientation.Vertical;
        var newGoesAfter = direction is SplitDirection.Right or SplitDirection.Down;

        var newTerminal = terminalFactory(workingDirectory);
        var newLeaf = new LeafPane(newTerminal);
        TrackLeaf(newLeaf);

        var parent = FindParent(root, target);
        int parentCell = 0;
        if (parent != null)
        {
            parentCell = parent.First == target ? 0 : 2;
            parent.GridView.Children.Remove(target.View);
        }
        else
        {
            RootView.Children.Remove(target.View);
        }

        var split = newGoesAfter
            ? new SplitPane(orientation, target, newLeaf, ratio)
            : new SplitPane(orientation, newLeaf, target, ratio);
        split.RatioChanged += () => LayoutChanged?.Invoke();

        if (parent != null)
        {
            parent.PlaceChild(split, parentCell);
            if (parent.First == target)
            {
                parent.First = split;
            }
            else
            {
                parent.Second = split;
            }
        }
        else
        {
            RootView.Children.Add(split.View);
            root = split;
        }

        SetFocusedLeaf(newLeaf);
        newTerminal.Focus();
        LayoutChanged?.Invoke();

        return newLeaf;
    }

    public bool Close(LeafPane target)
    {
        target.Terminal.Close();

        if (root == target)
        {
            RootView.Children.Clear();
            return true;
        }

        var parent = FindParent(root, target)!;
        var sibling = parent.First == target ? parent.Second : parent.First;
        parent.GridView.Children.Remove(sibling.View);

        var grandparent = FindParent(root, parent);
        if (grandparent == null)
        {
            RootView.Children.Remove(parent.View);
            RootView.Children.Add(sibling.View);
            root = sibling;
        }
        else
        {
            int cellIndex = grandparent.First == parent ? 0 : 2;
            grandparent.GridView.Children.Remove(parent.View);
            grandparent.PlaceChild(sibling, cellIndex);
            if (grandparent.First == parent)
            {
                grandparent.First = sibling;
            }
            else
            {
                grandparent.Second = sibling;
            }
        }

        if (focusedLeaf == target)
        {
            var newFocus = FirstLeaf(sibling);
            SetFocusedLeaf(newFocus);
            newFocus.Terminal.Focus();
        }

        LayoutChanged?.Invoke();
        return false;
    }

    public void DisposeAll()
    {
        DisposeNode(root);
        RootView.Children.Clear();
    }

    void DisposeNode(PaneNode node)
    {
        switch (node)
        {
            case LeafPane leaf:
                leaf.Terminal.Close();
                break;
            case SplitPane split:
                DisposeNode(split.First);
                DisposeNode(split.Second);
                break;
        }
    }

    void TrackLeaf(LeafPane leaf)
    {
        leaf.Terminal.GotFocus += (_, _) => SetFocusedLeaf(leaf);
        leaf.Terminal.WorkingDirectoryChanged += () => LayoutChanged?.Invoke();
    }

    void SetFocusedLeaf(LeafPane leaf)
    {
        if (focusedLeaf == leaf)
        {
            return;
        }
        focusedLeaf = leaf;
        FocusedLeafChanged?.Invoke();
    }

    static SplitPane? FindParent(PaneNode current, PaneNode target)
    {
        if (current is not SplitPane split)
        {
            return null;
        }
        if (split.First == target || split.Second == target)
        {
            return split;
        }
        return FindParent(split.First, target) ?? FindParent(split.Second, target);
    }

    static LeafPane? FindLeaf(PaneNode node, IPaneTerminal terminal)
    {
        return node switch
        {
            LeafPane leaf when leaf.Terminal == terminal => leaf,
            SplitPane split => FindLeaf(split.First, terminal) ?? FindLeaf(split.Second, terminal),
            _ => null,
        };
    }

    static LeafPane FirstLeaf(PaneNode node)
    {
        while (node is SplitPane split)
        {
            node = split.First;
        }
        return (LeafPane)node;
    }
}
