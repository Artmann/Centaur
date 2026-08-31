using System.Text;
using Avalonia.Input;
using Centaur.Core.Hosting;

namespace Centaur.App;

/// <summary>
/// What the user sends to the shell: a key turned into the bytes the pty expects, and the
/// bookkeeping that hangs off it - dismissing a stale suggestion, and recording the command
/// on Enter.
///
/// Split out of <see cref="TerminalControl"/> because none of it is Avalonia's business
/// beyond the key itself; the control keeps the routing and marks the key handled.
/// </summary>
public sealed class TerminalInput
{
    readonly ShellChannel shell;
    readonly InlineSuggestions suggestions;
    readonly ITerminalEvents events;

    public TerminalInput(ShellChannel shell, InlineSuggestions suggestions, ITerminalEvents events)
    {
        this.shell = shell;
        this.suggestions = suggestions;
        this.events = events;
    }

    /// <summary>The bytes a key press sends, or null when the key has nothing to send.</summary>
    public byte[]? Encode(Key key, KeyModifiers modifiers) =>
        modifiers.HasFlag(KeyModifiers.Control)
            ? ControlByteFor(key)
            : EncodeTypedKey(key, modifiers);

    /// <summary>Tab accepts the inline suggestion, or declines so it reaches the shell as a
    /// tab - which is what the user wanted when there is nothing to accept.</summary>
    public bool AcceptSuggestion()
    {
        var ghost = suggestions.TakeGhost();
        if (ghost == null)
        {
            return false;
        }

        shell.Send(Encoding.UTF8.GetBytes(ghost));
        return true;
    }

    /// <summary>A command picked out of reverse search is treated exactly like one the user
    /// typed: it joins the history and is sent with its Enter already attached.</summary>
    public void RunCommand(string command)
    {
        events.Publish(new CommandSubmittedEvent(command));
        shell.Send(Encoding.UTF8.GetBytes(command + "\r"));
    }

    // Ctrl+A is 0x01, Ctrl+C (with nothing selected) 0x03, and so on through Ctrl+Z. Any
    // other Ctrl combination has no byte of its own and is left unsent.
    byte[]? ControlByteFor(Key key)
    {
        if (key is < Key.A or > Key.Z)
        {
            return null;
        }

        suggestions.Clear();
        return [(byte)(key - Key.A + 1)];
    }

    // The unmodified path. Suggestion bookkeeping happens here rather than in the encoder
    // because it depends on what the pane knows - the typed line, and whether it is read-only.
    byte[]? EncodeTypedKey(Key key, KeyModifiers modifiers)
    {
        if (key == Key.Enter && !shell.IsReadOnly)
        {
            CaptureSubmittedCommand();
        }

        if (
            key
            is Key.Up
                or Key.Down
                or Key.Escape
                or Key.Back
                or Key.Delete
                or Key.Left
                or Key.Home
                or Key.End
        )
        {
            suggestions.Clear();
        }

        return TerminalKeyEncoder.Encode(key, modifiers);
    }

    // Enter is the only moment the typed line is still on screen and known to be complete,
    // so history and directory tracking both hang off it.
    void CaptureSubmittedCommand()
    {
        var input = suggestions.ReadTypedInput();
        if (!string.IsNullOrWhiteSpace(input))
        {
            events.Publish(new CommandSubmittedEvent(input.Trim()));
            shell.NoteCommandSubmitted(input.Trim());
        }

        suggestions.NoteCommandSubmitted();
    }
}
