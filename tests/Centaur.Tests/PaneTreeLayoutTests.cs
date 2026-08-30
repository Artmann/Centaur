using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Centaur.App.Splits;
using Xunit;

namespace Centaur.Tests;

/// <summary>The grid the pane tree builds: cell placement, split ratios, and the
/// LayoutChanged notifications that drive session saving.</summary>
public class PaneTreeLayoutTests
{
    [AvaloniaFact]
    public void Split_horizontal_places_children_in_columns_0_and_2()
    {
        var (tree, created) = FakePaneTerminal.BuildTree();
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
        var (tree, created) = FakePaneTerminal.BuildTree();
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
        var (tree, _) = FakePaneTerminal.BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var rightLeaf = (LeafPane)((SplitPane)tree.Root).Second;
        tree.Split(rightLeaf, SplitDirection.Down); // rightLeaf now at Grid.Row=0 in inner split
        var bottomLeaf = (LeafPane)((SplitPane)((SplitPane)tree.Root).Second).Second;

        tree.Close(rightLeaf); // bottomLeaf gets promoted into rootSplit.Second (column 2)

        Assert.Same(bottomLeaf, ((SplitPane)tree.Root).Second);
        Assert.Equal(2, Grid.GetColumn(bottomLeaf.View));
    }

    [AvaloniaTheory]
    [InlineData(Orientation.Horizontal)]
    [InlineData(Orientation.Vertical)]
    public void SplitPane_ratio_sizes_star_definitions(Orientation orientation)
    {
        var first = new LeafPane(new FakePaneTerminal());
        var second = new LeafPane(new FakePaneTerminal());

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
        var first = new LeafPane(new FakePaneTerminal());
        var second = new LeafPane(new FakePaneTerminal());

        var split = new SplitPane(Orientation.Horizontal, first, second, ratio: ratio);

        var firstWeight = split.GridView.ColumnDefinitions[0].Width.Value;
        Assert.InRange(firstWeight, 0.05, 0.95);
    }

    [AvaloniaFact]
    public void SplitPane_RatioChanged_fires_on_splitter_DragCompleted()
    {
        var first = new LeafPane(new FakePaneTerminal());
        var second = new LeafPane(new FakePaneTerminal());
        var split = new SplitPane(Orientation.Horizontal, first, second);
        var fired = 0;
        split.RatioChanged += () => fired++;
        DragSplitter(split);

        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void PaneTree_LayoutChanged_fires_on_split_and_close()
    {
        var (tree, _) = FakePaneTerminal.BuildTree();
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
        var (tree, created) = FakePaneTerminal.BuildTree();
        var fired = 0;
        tree.LayoutChanged += () => fired++;

        created[0].ChangeWorkingDirectory(@"C:\elsewhere");

        Assert.Equal(1, fired);
    }

    [AvaloniaFact]
    public void PaneTree_LayoutChanged_fires_on_ratio_change_of_new_split()
    {
        var (tree, _) = FakePaneTerminal.BuildTree();
        var initial = (LeafPane)tree.Root;
        tree.Split(initial, SplitDirection.Right);
        var split = (SplitPane)tree.Root;
        var fired = 0;
        tree.LayoutChanged += () => fired++;
        DragSplitter(split);

        Assert.Equal(1, fired);
    }

    /// <summary>Completes a drag on the split's GridSplitter, which is what commits a ratio change.</summary>
    static void DragSplitter(SplitPane split)
    {
        var splitter = split.GridView.Children.OfType<GridSplitter>().Single();

        splitter.RaiseEvent(
            new VectorEventArgs
            {
                RoutedEvent = Thumb.DragCompletedEvent,
                Vector = new Vector(20, 0),
            }
        );
    }
}
