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

        DefineCells(Math.Clamp(ratio, minRatio, maxRatio));
        SetCell(first.View, 0);
        SetCell(second.View, 2);

        var splitter = CreateSplitter();
        var divider = CreateDivider();
        SetCell(splitter, 1);
        SetCell(divider, 1);

        GridView.Children.Add(first.View);
        GridView.Children.Add(splitter);
        GridView.Children.Add(divider);
        GridView.Children.Add(second.View);

        View = GridView;
    }

    /// <summary>Lays out the three cells: the two panes with the gutter between them.</summary>
    void DefineCells(double ratio)
    {
        if (Orientation == Orientation.Horizontal)
        {
            GridView.ColumnDefinitions.Add(new ColumnDefinition(ratio, GridUnitType.Star));
            GridView.ColumnDefinitions.Add(
                new ColumnDefinition(gutterThickness, GridUnitType.Pixel)
            );
            GridView.ColumnDefinitions.Add(new ColumnDefinition(1 - ratio, GridUnitType.Star));
        }
        else
        {
            GridView.RowDefinitions.Add(new RowDefinition(ratio, GridUnitType.Star));
            GridView.RowDefinitions.Add(new RowDefinition(gutterThickness, GridUnitType.Pixel));
            GridView.RowDefinitions.Add(new RowDefinition(1 - ratio, GridUnitType.Star));
        }
    }

    /// <summary>Assigns a child to one of the three cells along the split's axis.</summary>
    void SetCell(Control view, int cellIndex)
    {
        if (Orientation == Orientation.Horizontal)
        {
            Grid.SetColumn(view, cellIndex);
        }
        else
        {
            Grid.SetRow(view, cellIndex);
        }
    }

    GridSplitter CreateSplitter()
    {
        var splitter = new GridSplitter
        {
            ResizeDirection =
                Orientation == Orientation.Horizontal
                    ? GridResizeDirection.Columns
                    : GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            Background = gutterBrush,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        splitter.DragCompleted += (_, _) => RatioChanged?.Invoke();
        return splitter;
    }

    /// <summary>The hairline drawn down the middle of the gutter. It sits above the splitter
    /// but takes no hits, so the gutter stays draggable across its full width.</summary>
    Border CreateDivider() =>
        Orientation == Orientation.Horizontal
            ? new Border
            {
                Background = dividerBrush,
                Width = dividerThickness,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Stretch,
                IsHitTestVisible = false,
            }
            : new Border
            {
                Background = dividerBrush,
                Height = dividerThickness,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false,
            };

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
        SetCell(child.View, cellIndex);

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
        return PaneNodes.Find(root, terminal);
    }

    public LeafPane Split(
        LeafPane target,
        SplitDirection direction,
        string? workingDirectory = null,
        double ratio = 0.5
    )
    {
        var newTerminal = terminalFactory(workingDirectory);
        var newLeaf = new LeafPane(newTerminal);
        TrackLeaf(newLeaf);

        var parent = Detach(target);
        var split = CreateSplit(target, newLeaf, direction, ratio);
        Replace(parent, target, split);

        SetFocusedLeaf(newLeaf);
        newTerminal.Focus();
        LayoutChanged?.Invoke();

        return newLeaf;
    }

    /// <summary>Pairs the existing pane with the new one, on the axis and in the order the
    /// direction asks for.</summary>
    SplitPane CreateSplit(LeafPane target, LeafPane newLeaf, SplitDirection direction, double ratio)
    {
        var orientation = direction is SplitDirection.Right or SplitDirection.Left
            ? Orientation.Horizontal
            : Orientation.Vertical;
        var newGoesAfter = direction is SplitDirection.Right or SplitDirection.Down;

        var split = newGoesAfter
            ? new SplitPane(orientation, target, newLeaf, ratio)
            : new SplitPane(orientation, newLeaf, target, ratio);
        split.RatioChanged += () => LayoutChanged?.Invoke();
        return split;
    }

    /// <summary>Lifts a node out of the visual tree, handing back the split it hung from —
    /// null when it was the root.</summary>
    SplitPane? Detach(PaneNode node)
    {
        var parent = PaneNodes.Parent(root, node);
        if (parent == null)
        {
            RootView.Children.Remove(node.View);
        }
        else
        {
            parent.GridView.Children.Remove(node.View);
        }

        return parent;
    }

    /// <summary>Hangs <paramref name="replacement"/> in the slot <paramref name="detached"/>
    /// came out of, promoting it to root when there is no parent.</summary>
    void Replace(SplitPane? parent, PaneNode detached, PaneNode replacement)
    {
        if (parent == null)
        {
            RootView.Children.Add(replacement.View);
            root = replacement;
            return;
        }

        if (parent.First == detached)
        {
            parent.First = replacement;
            parent.PlaceChild(replacement, 0);
        }
        else
        {
            parent.Second = replacement;
            parent.PlaceChild(replacement, 2);
        }
    }

    public bool Close(LeafPane target)
    {
        target.Terminal.Close();

        if (root == target)
        {
            RootView.Children.Clear();
            return true;
        }

        // Closing one half of a split dissolves the split itself: the sibling moves up into
        // the space the two of them shared.
        var parent = PaneNodes.Parent(root, target)!;
        var sibling = parent.First == target ? parent.Second : parent.First;
        parent.GridView.Children.Remove(sibling.View);
        Replace(Detach(parent), parent, sibling);

        if (focusedLeaf == target)
        {
            var newFocus = PaneNodes.FirstLeaf(sibling);
            SetFocusedLeaf(newFocus);
            newFocus.Terminal.Focus();
        }

        LayoutChanged?.Invoke();
        return false;
    }

    public void DisposeAll()
    {
        PaneNodes.CloseAll(root);
        RootView.Children.Clear();
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
}
