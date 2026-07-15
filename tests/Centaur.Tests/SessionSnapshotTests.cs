using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Centaur.App;
using Centaur.App.Splits;
using Xunit;

namespace Centaur.Tests;

public class SessionSnapshotTests
{
    sealed class FakeTerminal : IPaneTerminal
    {
        public Control View { get; } = new Panel();
        public string? WorkingDirectory { get; private set; }

#pragma warning disable CS0067 // required by IPaneTerminal but unused by these snapshot tests
        public event EventHandler<GotFocusEventArgs>? GotFocus;
        public event Action? WorkingDirectoryChanged;
#pragma warning restore CS0067

        public FakeTerminal(string? workingDirectory = null)
        {
            WorkingDirectory = workingDirectory;
        }

        public bool Focus() => true;

        public void Close() { }
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
    public void Capture_single_leaf_returns_leaf_node()
    {
        var (tree, _) = BuildTree(@"C:\a");

        var node = SessionSnapshot.Capture(tree.Root);

        Assert.False(node.IsSplit);
        Assert.Equal(@"C:\a", node.WorkingDirectory);
    }

    [AvaloniaFact]
    public void Capture_restore_round_trips_a_single_split()
    {
        var (sourceTree, _) = BuildTree(@"C:\a");
        var initial = (LeafPane)sourceTree.Root;
        sourceTree.Split(initial, SplitDirection.Right, workingDirectory: @"C:\b", ratio: 0.3);

        var captured = SessionSnapshot.Capture(sourceTree.Root);

        Assert.True(captured.IsSplit);
        Assert.Equal(Orientation.Horizontal, captured.Orientation);
        Assert.Equal(0.3, captured.Ratio, 2);
        Assert.Equal(@"C:\a", captured.First!.WorkingDirectory);
        Assert.Equal(@"C:\b", captured.Second!.WorkingDirectory);

        var cwd = SessionSnapshot.FirstLeafWorkingDirectory(captured);
        var (targetTree, _) = BuildTree(cwd);
        SessionSnapshot.RestoreInto(captured, (LeafPane)targetTree.Root, targetTree);

        var restored = Assert.IsType<SplitPane>(targetTree.Root);
        Assert.Equal(Orientation.Horizontal, restored.Orientation);
        Assert.Equal(0.3, restored.ComputeRatio(), 2);
        Assert.Equal(@"C:\a", ((LeafPane)restored.First).Terminal.WorkingDirectory);
        Assert.Equal(@"C:\b", ((LeafPane)restored.Second).Terminal.WorkingDirectory);
    }

    [AvaloniaFact]
    public void Capture_restore_round_trips_a_three_level_nested_mixed_orientation_tree()
    {
        // Root: Horizontal(A, Vertical(B, Horizontal(C, D)))
        var (sourceTree, _) = BuildTree(@"C:\A");
        var a = (LeafPane)sourceTree.Root;
        var b = sourceTree.Split(a, SplitDirection.Right, workingDirectory: @"C:\B", ratio: 0.5);
        var c = sourceTree.Split(b, SplitDirection.Down, workingDirectory: @"C:\C", ratio: 0.4);
        sourceTree.Split(c, SplitDirection.Right, workingDirectory: @"C:\D", ratio: 0.6);

        var captured = SessionSnapshot.Capture(sourceTree.Root);
        var cwd = SessionSnapshot.FirstLeafWorkingDirectory(captured);
        var (targetTree, _) = BuildTree(cwd);
        SessionSnapshot.RestoreInto(captured, (LeafPane)targetTree.Root, targetTree);

        var root = Assert.IsType<SplitPane>(targetTree.Root);
        Assert.Equal(Orientation.Horizontal, root.Orientation);
        Assert.Equal(0.5, root.ComputeRatio(), 2);
        Assert.Equal(@"C:\A", ((LeafPane)root.First).Terminal.WorkingDirectory);

        var middle = Assert.IsType<SplitPane>(root.Second);
        Assert.Equal(Orientation.Vertical, middle.Orientation);
        Assert.Equal(0.4, middle.ComputeRatio(), 2);
        Assert.Equal(@"C:\B", ((LeafPane)middle.First).Terminal.WorkingDirectory);

        var inner = Assert.IsType<SplitPane>(middle.Second);
        Assert.Equal(Orientation.Horizontal, inner.Orientation);
        Assert.Equal(0.6, inner.ComputeRatio(), 2);
        Assert.Equal(@"C:\C", ((LeafPane)inner.First).Terminal.WorkingDirectory);
        Assert.Equal(@"C:\D", ((LeafPane)inner.Second).Terminal.WorkingDirectory);
    }

    [AvaloniaFact]
    public void RestoreInto_throws_when_split_node_is_missing_children()
    {
        var (targetTree, _) = BuildTree(@"C:\a");
        var malformed = new SessionNode { IsSplit = true, Orientation = Orientation.Horizontal };

        Assert.Throws<InvalidOperationException>(() =>
            SessionSnapshot.RestoreInto(malformed, (LeafPane)targetTree.Root, targetTree)
        );
    }

    [AvaloniaFact]
    public void FirstLeafWorkingDirectory_throws_when_split_node_is_missing_first_child()
    {
        var malformed = new SessionNode { IsSplit = true, Orientation = Orientation.Horizontal };

        Assert.Throws<InvalidOperationException>(() =>
            SessionSnapshot.FirstLeafWorkingDirectory(malformed)
        );
    }
}
