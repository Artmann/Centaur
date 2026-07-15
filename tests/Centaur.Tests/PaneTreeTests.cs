using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Centaur.App.Splits;
using Xunit;

namespace Centaur.Tests;

public class PaneTreeTests
{
    sealed class FakeTerminal : IPaneTerminal
    {
        public Control View { get; } = new Panel();
        public int FocusCalls { get; private set; }
        public bool Closed { get; private set; }
        public string? WorkingDirectory { get; private set; }

        public event EventHandler<GotFocusEventArgs>? GotFocus;
        public event Action? WorkingDirectoryChanged;

        public FakeTerminal(string? workingDirectory = null)
        {
            WorkingDirectory = workingDirectory;
        }

        public bool Focus()
        {
            FocusCalls++;
            return true;
        }

        public void Close() => Closed = true;

        public void RaiseGotFocus()
        {
            GotFocus?.Invoke(this, new GotFocusEventArgs());
        }

        public void ChangeWorkingDirectory(string directory)
        {
            WorkingDirectory = directory;
            WorkingDirectoryChanged?.Invoke();
        }
    }

    static (PaneTree tree, List<FakeTerminal> created) BuildTree(
        string? initialWorkingDirectory = null
    )
    {
        var created = new List<FakeTerminal>();
        var tree = new PaneTree(
            cwd =>
            {
                var t = new FakeTerminal(cwd);
                created.Add(t);
                return t;
            },
            initialWorkingDirectory
        );
        return (tree, created);
    }

    [AvaloniaFact]
    public void Initial_tree_is_single_leaf_focused_on_first_terminal()
    {
        var (tree, created) = BuildTree();

        Assert.Single(created);
        var leaf = Assert.IsType<LeafPane>(tree.Root);
        Assert.Same(created[0], leaf.Terminal);
        Assert.Same(leaf, tree.FocusedLeaf);
    }

    [AvaloniaFact]
    public void Initial_rootView_contains_terminal_view()
    {
        var (tree, created) = BuildTree();

        Assert.Single(tree.RootView.Children);
        Assert.Same(created[0].View, tree.RootView.Children[0]);
    }

    [AvaloniaTheory]
    [InlineData(SplitDirection.Right, Orientation.Horizontal, true)]
    [InlineData(SplitDirection.Down, Orientation.Vertical, true)]
    [InlineData(SplitDirection.Left, Orientation.Horizontal, false)]
    [InlineData(SplitDirection.Up, Orientation.Vertical, false)]
    public void Split_creates_expected_orientation_and_order(
        SplitDirection direction,
        Orientation expectedOrientation,
        bool newGoesSecond
    )
    {
        var (tree, _) = BuildTree();
        var original = (LeafPane)tree.Root;

        tree.Split(original, direction);

        var split = Assert.IsType<SplitPane>(tree.Root);
        Assert.Equal(expectedOrientation, split.Orientation);
        if (newGoesSecond)
        {
            Assert.Same(original, split.First);
            Assert.IsType<LeafPane>(split.Second);
        }
        else
        {
            Assert.IsType<LeafPane>(split.First);
            Assert.Same(original, split.Second);
        }
    }

    [AvaloniaFact]
    public void Split_focuses_and_calls_Focus_on_new_terminal()
    {
        var (tree, created) = BuildTree();
        var fired = 0;
        tree.FocusedLeafChanged += () => fired++;

        tree.Split((LeafPane)tree.Root, SplitDirection.Right);

        Assert.Equal(2, created.Count);
        var newTerminal = created[1];
        Assert.Equal(1, newTerminal.FocusCalls);
        Assert.Same(newTerminal, tree.FocusedLeaf.Terminal);
        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void Split_replaces_root_view_with_split_grid()
    {
        var (tree, _) = BuildTree();
        var original = (LeafPane)tree.Root;

        tree.Split(original, SplitDirection.Right);

        Assert.Single(tree.RootView.Children);
        var grid = Assert.IsType<Grid>(tree.RootView.Children[0]);
        Assert.Same(((SplitPane)tree.Root).GridView, grid);
        Assert.Contains(original.View, grid.Children);
    }

    [AvaloniaFact]
    public void Nested_split_in_first_slot_keeps_grandparent_intact()
    {
        var (tree, created) = BuildTree();
        var initial = (LeafPane)tree.Root;

        tree.Split(initial, SplitDirection.Right); // Root = Split(initial, B)
        var rootSplit = (SplitPane)tree.Root;
        var sibling = (LeafPane)rootSplit.Second;

        tree.Split(initial, SplitDirection.Down); // initial is now in a nested vertical split

        // Root unchanged
        Assert.Same(rootSplit, tree.Root);
        // First slot now holds a vertical SplitPane containing the old initial leaf
        var nested = Assert.IsType<SplitPane>(rootSplit.First);
        Assert.Equal(Orientation.Vertical, nested.Orientation);
        Assert.Same(initial, nested.First);
        Assert.Same(created[2], ((LeafPane)nested.Second).Terminal);
        // Sibling untouched
        Assert.Same(sibling, rootSplit.Second);
        // Nested grid is correctly placed inside the parent grid
        Assert.Contains(nested.View, rootSplit.GridView.Children);
    }

    [AvaloniaFact]
    public void Nested_split_in_second_slot_replaces_only_the_second_child()
    {
        var (tree, _) = BuildTree();
        var initial = (LeafPane)tree.Root;

        tree.Split(initial, SplitDirection.Right);
        var rootSplit = (SplitPane)tree.Root;
        var rightLeaf = (LeafPane)rootSplit.Second;

        tree.Split(rightLeaf, SplitDirection.Up);

        Assert.Same(initial, rootSplit.First);
        var nested = Assert.IsType<SplitPane>(rootSplit.Second);
        Assert.Equal(Orientation.Vertical, nested.Orientation);
        Assert.Same(rightLeaf, nested.Second);
    }

    [AvaloniaFact]
    public void LeafFor_finds_leaf_by_terminal_identity()
    {
        var (tree, created) = BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);

        Assert.Same(initial, tree.LeafFor(created[0]));
        Assert.NotNull(tree.LeafFor(created[1]));
        Assert.Null(tree.LeafFor(new FakeTerminal()));
    }

    [AvaloniaFact]
    public void Close_only_leaf_returns_true_and_disposes_it()
    {
        var (tree, created) = BuildTree();
        var only = (LeafPane)tree.Root;

        var empty = tree.Close(only);

        Assert.True(empty);
        Assert.True(created[0].Closed);
        Assert.Empty(tree.RootView.Children);
    }

    [AvaloniaFact]
    public void Close_one_of_two_leaves_promotes_sibling_to_root()
    {
        var (tree, created) = BuildTree();
        var first = (LeafPane)tree.Root;
        tree.Split(first, SplitDirection.Right);
        var second = (LeafPane)((SplitPane)tree.Root).Second;

        var empty = tree.Close(second);

        Assert.False(empty);
        Assert.Same(first, tree.Root);
        Assert.True(created[1].Closed);
        Assert.False(created[0].Closed);
        Assert.Single(tree.RootView.Children);
        Assert.Same(first.View, tree.RootView.Children[0]);
    }

    [AvaloniaFact]
    public void Close_in_nested_split_promotes_sibling_into_grandparent_slot()
    {
        var (tree, _) = BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var rootSplit = (SplitPane)tree.Root;
        var rightLeaf = (LeafPane)rootSplit.Second;
        tree.Split(rightLeaf, SplitDirection.Down);
        var rightSplit = (SplitPane)rootSplit.Second;
        var bottomLeaf = (LeafPane)rightSplit.Second;

        tree.Close(rightLeaf); // collapse the right vertical split

        // The right subtree now is just bottomLeaf, sitting in rootSplit.Second
        Assert.Same(bottomLeaf, rootSplit.Second);
        Assert.Same(initial, rootSplit.First);
        Assert.Contains(bottomLeaf.View, rootSplit.GridView.Children);
        Assert.DoesNotContain(rightSplit.View, rootSplit.GridView.Children);
    }

    [AvaloniaFact]
    public void Close_focused_leaf_moves_focus_to_first_leaf_of_sibling_subtree()
    {
        var (tree, created) = BuildTree();
        var first = (LeafPane)tree.Root;
        tree.Split(first, SplitDirection.Right); // focus now on the new (right) leaf
        var second = (LeafPane)((SplitPane)tree.Root).Second;
        tree.Split(second, SplitDirection.Down); // focus now on the new (bottom-right) leaf
        var focused = tree.FocusedLeaf;
        Assert.Same(created[2], focused.Terminal);
        var siblingOfFocused = ((SplitPane)((SplitPane)tree.Root).Second).First;

        tree.Close(focused);

        // Sibling (the top-right leaf) should now be focused
        Assert.Same(siblingOfFocused, tree.FocusedLeaf);
        Assert.True(((FakeTerminal)((LeafPane)siblingOfFocused).Terminal).FocusCalls >= 1);
    }

    [AvaloniaFact]
    public void Close_unfocused_leaf_keeps_focus_where_it_was()
    {
        var (tree, _) = BuildTree();
        var first = (LeafPane)tree.Root;
        tree.Split(first, SplitDirection.Right);
        var second = (LeafPane)((SplitPane)tree.Root).Second;
        // focus is currently on `second` (the newly split pane)

        tree.Close(first);

        Assert.Same(second, tree.FocusedLeaf);
    }

    [AvaloniaFact]
    public void GotFocus_event_updates_focused_leaf_and_fires_change()
    {
        var (tree, _) = BuildTree();
        var first = (LeafPane)tree.Root;
        tree.Split(first, SplitDirection.Right); // FocusedLeaf is now `second`
        var changes = 0;
        tree.FocusedLeafChanged += () => changes++;

        ((FakeTerminal)first.Terminal).RaiseGotFocus();

        Assert.Same(first, tree.FocusedLeaf);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void DisposeAll_closes_every_terminal_and_clears_root_view()
    {
        var (tree, created) = BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var rightLeaf = (LeafPane)((SplitPane)tree.Root).Second;
        tree.Split(rightLeaf, SplitDirection.Down);

        tree.DisposeAll();

        Assert.Equal(3, created.Count);
        Assert.All(created, t => Assert.True(t.Closed));
        Assert.Empty(tree.RootView.Children);
    }

    [AvaloniaFact]
    public void Split_horizontal_places_children_in_columns_0_and_2()
    {
        var (tree, created) = BuildTree();
        var initial = (LeafPane)tree.Root;

        tree.Split(initial, SplitDirection.Right);
        var split = (SplitPane)tree.Root;

        Assert.Equal(0, Grid.GetColumn(initial.View));
        Assert.Equal(2, Grid.GetColumn(created[1].View));
        Assert.Equal(3, split.GridView.ColumnDefinitions.Count);
    }

    [AvaloniaFact]
    public void Split_vertical_places_children_in_rows_0_and_2()
    {
        var (tree, created) = BuildTree();
        var initial = (LeafPane)tree.Root;

        tree.Split(initial, SplitDirection.Down);
        var split = (SplitPane)tree.Root;

        Assert.Equal(0, Grid.GetRow(initial.View));
        Assert.Equal(2, Grid.GetRow(created[1].View));
        Assert.Equal(3, split.GridView.RowDefinitions.Count);
    }

    [AvaloniaFact]
    public void Closing_promoted_sibling_resets_grid_attached_properties()
    {
        // Sibling moves from a vertical split (Grid.Row=0) into a horizontal split slot
        // — verify Grid.SetColumn is applied so it lands in the right cell.
        var (tree, _) = BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var rightLeaf = (LeafPane)((SplitPane)tree.Root).Second;
        tree.Split(rightLeaf, SplitDirection.Down); // rightLeaf now at Grid.Row=0 in inner split
        var bottomLeaf = (LeafPane)((SplitPane)((SplitPane)tree.Root).Second).Second;

        tree.Close(rightLeaf); // bottomLeaf gets promoted into rootSplit.Second (column 2)

        Assert.Same(bottomLeaf, ((SplitPane)tree.Root).Second);
        Assert.Equal(2, Grid.GetColumn(bottomLeaf.View));
    }

    [AvaloniaFact]
    public void Constructor_honors_initial_working_directory()
    {
        var (tree, created) = BuildTree(@"C:\repo");

        Assert.Equal(@"C:\repo", created[0].WorkingDirectory);
        Assert.Equal(@"C:\repo", ((LeafPane)tree.Root).Terminal.WorkingDirectory);
    }

    [AvaloniaFact]
    public void Split_returns_new_leaf_and_honors_working_directory()
    {
        var (tree, created) = BuildTree();
        var original = (LeafPane)tree.Root;

        var newLeaf = tree.Split(original, SplitDirection.Right, workingDirectory: @"C:\new");

        Assert.Same(newLeaf, ((SplitPane)tree.Root).Second);
        Assert.Same(created[1], newLeaf.Terminal);
        Assert.Equal(@"C:\new", created[1].WorkingDirectory);
    }

    [AvaloniaTheory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void SplitPane_ratio_sizes_star_definitions(Orientation orientation)
    {
        var first = new LeafPane(new FakeTerminal());
        var second = new LeafPane(new FakeTerminal());

        var split = new SplitPane(orientation, first, second, ratio: 0.3);

        if (orientation == Orientation.Horizontal)
        {
            Assert.Equal(0.3, split.GridView.ColumnDefinitions[0].Width.Value, 3);
            Assert.Equal(0.7, split.GridView.ColumnDefinitions[2].Width.Value, 3);
        }
        else
        {
            Assert.Equal(0.3, split.GridView.RowDefinitions[0].Height.Value, 3);
            Assert.Equal(0.7, split.GridView.RowDefinitions[2].Height.Value, 3);
        }
    }

    [AvaloniaTheory]
    [InlineData(1.5)]
    [InlineData(-0.5)]
    [InlineData(0)]
    [InlineData(1)]
    public void SplitPane_clamps_out_of_range_ratios(double ratio)
    {
        var first = new LeafPane(new FakeTerminal());
        var second = new LeafPane(new FakeTerminal());

        var split = new SplitPane(Orientation.Horizontal, first, second, ratio: ratio);

        var firstWeight = split.GridView.ColumnDefinitions[0].Width.Value;
        Assert.InRange(firstWeight, 0.05, 0.95);
    }

    [AvaloniaFact]
    public void SplitPane_RatioChanged_fires_on_splitter_DragCompleted()
    {
        var first = new LeafPane(new FakeTerminal());
        var second = new LeafPane(new FakeTerminal());
        var split = new SplitPane(Orientation.Horizontal, first, second);
        var fired = 0;
        split.RatioChanged += () => fired++;
        var splitter = split.GridView.Children.OfType<GridSplitter>().Single();

        splitter.RaiseEvent(
            new VectorEventArgs
            {
                RoutedEvent = Thumb.DragCompletedEvent,
                Vector = new Vector(20, 0),
            }
        );

        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void PaneTree_LayoutChanged_fires_on_split_and_close()
    {
        var (tree, _) = BuildTree();
        var initial = (LeafPane)tree.Root;
        var fired = 0;
        tree.LayoutChanged += () => fired++;

        var newLeaf = tree.Split(initial, SplitDirection.Right);
        Assert.Equal(1, fired);

        tree.Close(newLeaf);
        Assert.Equal(2, fired);
    }

    [AvaloniaFact]
    public void PaneTree_LayoutChanged_fires_on_leaf_working_directory_change()
    {
        var (tree, created) = BuildTree();
        var fired = 0;
        tree.LayoutChanged += () => fired++;

        created[0].ChangeWorkingDirectory(@"C:\elsewhere");

        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void PaneTree_LayoutChanged_fires_on_ratio_change_of_new_split()
    {
        var (tree, _) = BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var split = (SplitPane)tree.Root;
        var fired = 0;
        tree.LayoutChanged += () => fired++;
        var splitter = split.GridView.Children.OfType<GridSplitter>().Single();

        splitter.RaiseEvent(
            new VectorEventArgs
            {
                RoutedEvent = Thumb.DragCompletedEvent,
                Vector = new Vector(20, 0),
            }
        );

        Assert.Equal(1, fired);
    }
}
