using Avalonia.Controls;
using Avalonia.Input;
using Centaur.App.Splits;

namespace Centaur.Tests;

/// <summary>
/// Stand-in for TerminalControl in pane/split tests: no PTY, no rendering, just the
/// IPaneTerminal surface plus counters and event triggers the tests assert on.
/// </summary>
public sealed class FakePaneTerminal : IPaneTerminal
{
    public Control View { get; } = new Panel();
    public int FocusCalls { get; private set; }
    public bool Closed { get; private set; }
    public string? WorkingDirectory { get; private set; }

    public event EventHandler<GotFocusEventArgs>? GotFocus;
    public event Action? WorkingDirectoryChanged;

    public FakePaneTerminal(string? workingDirectory = null)
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

    /// <summary>
    /// Builds a PaneTree whose panes are FakePaneTerminals, returning them in creation order.
    /// </summary>
    public static (PaneTree tree, List<FakePaneTerminal> created) BuildTree(
        string? initialWorkingDirectory = null
    )
    {
        var created = new List<FakePaneTerminal>();
        var tree = new PaneTree(
            cwd =>
            {
                var terminal = new FakePaneTerminal(cwd);
                created.Add(terminal);
                return terminal;
            },
            initialWorkingDirectory
        );
        return (tree, created);
    }
}
