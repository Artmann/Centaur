using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The greyed-out completion shown ahead of the cursor, and the bookkeeping that decides when
/// it is allowed to appear.
///
/// The shell never tells us where its prompt ends, so we record the cursor column at the
/// moment output stops arriving and treat everything typed after it as the user's input.
/// That is also why suggestions stay off between pressing Enter and the next prompt landing:
/// until then the cursor is somewhere in the command's own output.
///
/// Shares the pane's buffer lock, because reading the typed line means reading the live grid.
/// </summary>
public sealed class InlineSuggestions
{
    readonly SuggestionState state;
    readonly VtParser parser;
    readonly object bufferLock;
    readonly Action markDirty;

    ISuggestionProvider? provider;
    int promptEndColumn;

    // True from Enter until the next prompt has finished being drawn.
    bool awaitingPrompt = true;

    public InlineSuggestions(
        SuggestionState state,
        VtParser parser,
        object bufferLock,
        Action markDirty
    )
    {
        this.state = state;
        this.parser = parser;
        this.bufferLock = bufferLock;
        this.markDirty = markDirty;
    }

    /// <summary>Where completions come from. Null disables suggestions entirely.</summary>
    public ISuggestionProvider? Provider
    {
        get => provider;
        set => provider = value;
    }

    /// <summary>
    /// Records where the prompt ends. Call from the PTY read loop with the buffer lock held:
    /// while output is still arriving, wherever the cursor stopped is the end of the prompt.
    /// </summary>
    public void NoteParsedOutput()
    {
        if (awaitingPrompt)
        {
            promptEndColumn = parser.ActiveBuffer.cursorX;
        }
    }

    /// <summary>The user has typed; the prompt is behind us and the suggestion can update.</summary>
    public void NoteTypedText(string text)
    {
        awaitingPrompt = false;
        Refresh(text);
    }

    /// <summary>A command was submitted, so wait for the next prompt before suggesting again.</summary>
    public void NoteCommandSubmitted()
    {
        state.Clear();
        awaitingPrompt = true;
    }

    /// <summary>The current suggestion, consumed - null when there is nothing to accept.</summary>
    public string? TakeGhost()
    {
        var (ghost, _, _) = state.Read();
        if (string.IsNullOrEmpty(ghost))
        {
            return null;
        }

        state.Clear();
        return ghost;
    }

    public void Clear()
    {
        state.Clear();
    }

    /// <summary>Everything typed since the prompt ended, trailing spaces trimmed.</summary>
    public string ReadTypedInput()
    {
        lock (bufferLock)
        {
            var buffer = parser.ActiveBuffer;
            var length = buffer.cursorX - promptEndColumn;
            if (length <= 0)
            {
                return string.Empty;
            }

            var row = buffer.GetRow(buffer.cursorY);
            var chars = new char[length];
            for (var i = 0; i < length; i++)
            {
                chars[i] = row[promptEndColumn + i].character;
            }

            return new string(chars).TrimEnd();
        }
    }

    /// <param name="appendedText">
    /// Text the user just typed that the shell has not echoed back yet, so it is not in the
    /// grid. It counts as input and shifts the ghost's column.
    /// </param>
    void Refresh(string? appendedText)
    {
        // No provider, or a full-screen program owns the display: nothing to complete.
        if (provider == null || parser.IsAlternateScreen || awaitingPrompt)
        {
            state.Clear();
            markDirty();
            return;
        }

        var input = ReadTypedInput() + appendedText;
        var match = provider.GetSuggestion(input);

        if (match != null && match.Length > input.Length)
        {
            var (column, row) = GhostPosition(appendedText);
            state.Update(match[input.Length..], column, row);
        }
        else
        {
            state.Clear();
        }

        markDirty();
    }

    // Where the ghost text starts. The shell has not echoed appendedText yet, so the cursor
    // is still sitting behind it.
    (int column, int row) GhostPosition(string? appendedText)
    {
        lock (bufferLock)
        {
            var buffer = parser.ActiveBuffer;
            return (buffer.cursorX + (appendedText?.Length ?? 0), buffer.cursorY);
        }
    }
}
