using System.Text;
using Avalonia;
using Avalonia.Controls;

namespace Centaur.App;

/// <summary>
/// Copy and paste for one pane: the selection out to the system clipboard, and clipboard text
/// in as if it had been typed.
///
/// Split out of <see cref="TerminalControl"/>, which keeps only the gestures that reach here.
/// The clipboard is owned by the window rather than the control, so it is looked up per call -
/// a pane that has been re-parented, or is not in the visual tree at all, simply does nothing.
/// </summary>
public sealed class TerminalClipboard
{
    readonly Visual owner;
    readonly TerminalSurface surface;
    readonly ShellChannel shell;
    readonly Action markDirty;

    public TerminalClipboard(
        Visual owner,
        TerminalSurface surface,
        ShellChannel shell,
        Action markDirty
    )
    {
        this.owner = owner;
        this.surface = surface;
        this.shell = shell;
        this.markDirty = markDirty;
    }

    /// <summary>Ctrl+C copies only when there is a selection; with none it declines, so the
    /// key goes to the shell as the interrupt the user meant.</summary>
    public bool CopyIfSelected()
    {
        if (!surface.Selection.HasSelection)
        {
            return false;
        }

        Copy();
        return true;
    }

    public async void Copy()
    {
        var text = surface.SelectedText();

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }

        surface.Selection.Clear();
        markDirty();
    }

    public async void Paste()
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        var text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Normalize line endings to \r for the terminal.
        text = text.Replace("\r\n", "\r").Replace("\n", "\r");

        shell.Send(Encoding.UTF8.GetBytes(text));
    }
}
