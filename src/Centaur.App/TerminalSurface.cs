using System.Buffers;
using Avalonia;
using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// A pane's terminal state and the view onto it: the parser and the screen buffers it writes
/// into, the scrollback viewport, and the selection drawn over the grid.
///
/// Split out of <see cref="TerminalControl"/> so the locking discipline lives in one place.
/// The pty read thread parses into the buffers while the render thread reads them, so every
/// method here takes the lock itself and nothing outside touches a buffer without it. The
/// control keeps what is genuinely Avalonia's: input routing, focus and redraws.
/// </summary>
public sealed class TerminalSurface
{
    readonly object bufferLock = new();
    readonly VtParser parser;

    // Only for cell metrics - turning a pixel position into a grid cell.
    readonly TerminalRenderer renderer;

    // Moving the viewport changes what is on screen, so the surface asks for the redraw
    // itself rather than making every caller remember to.
    readonly Action markDirty;

    public TerminalSurface(TerminalTheme theme, TerminalRenderer renderer, Action markDirty)
    {
        // Start at a default size; the control resizes once it knows its bounds.
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
        this.renderer = renderer;
        this.markDirty = markDirty;
    }

    /// <summary>The parser behind the surface. Reading a buffer through it means holding
    /// <see cref="BufferLock"/>.</summary>
    public VtParser Parser => parser;

    /// <summary>The lock every buffer read and write is made under, shared with the
    /// collaborators that read the live grid for themselves.</summary>
    public object BufferLock => bufferLock;

    /// <summary>Mouse text selection over the grid.</summary>
    public SelectionController Selection { get; } = new();

    /// <summary>Resizes both screens, reporting whether the grid actually changed shape.</summary>
    public bool ResizeTo(int columns, int rows)
    {
        lock (bufferLock)
        {
            if (columns == parser.ActiveBuffer.columns && rows == parser.ActiveBuffer.rows)
            {
                return false;
            }

            parser.Resize(columns, rows);
        }

        return true;
    }

    /// <summary>Feeds a chunk of pty output through the parser, on the read thread.
    /// <paramref name="parsed"/> runs while the lock is still held, for the bookkeeping that
    /// has to see the grid exactly as this chunk left it.</summary>
    public void Process(ReadOnlySequence<byte> bytes, Action parsed)
    {
        lock (bufferLock)
        {
            foreach (var segment in bytes)
            {
                parser.Process(segment.Span);
            }

            parsed();
        }
    }

    /// <summary>Puts the view back at the prompt, which is where a keystroke belongs.</summary>
    public void ScrollToLiveEdge()
    {
        lock (bufferLock)
        {
            parser.ActiveBuffer.Scrollback.ScrollToBottom();
        }

        markDirty();
    }

    /// <summary>Scrolls by wheel notches, three lines each.</summary>
    public bool ScrollByWheel(int delta) => Scroll(up: delta > 0, Math.Max(1, Math.Abs(delta) * 3));

    /// <summary>Scrolls a screenful, keeping one line of context.</summary>
    public bool ScrollByPage(bool up) => Scroll(up, parser.ActiveBuffer.rows - 1);

    /// <summary>Declines on the alternate screen, which keeps no scrollback and where
    /// full-screen programs expect the scroll keys themselves.</summary>
    bool Scroll(bool up, int lines)
    {
        if (parser.IsAlternateScreen)
        {
            return false;
        }

        lock (bufferLock)
        {
            var scrollback = parser.ActiveBuffer.Scrollback;
            if (up)
            {
                scrollback.ScrollUp(lines);
            }
            else
            {
                scrollback.ScrollDown(lines);
            }
        }

        Selection.Clear();
        markDirty();
        return true;
    }

    public void BeginDrag(Point position, int clickCount)
    {
        var (col, row) = CellAt(position);
        lock (bufferLock)
        {
            Selection.BeginDrag(parser.ActiveBuffer, col, row, clickCount);
        }
    }

    public void ExtendDrag(Point position)
    {
        var (col, row) = CellAt(position);
        lock (bufferLock)
        {
            Selection.ExtendDrag(parser.ActiveBuffer, col, row);
        }
    }

    public void EndDrag(Point position)
    {
        var (col, row) = CellAt(position);
        Selection.EndDrag(col, row);
    }

    /// <summary>The text the selection covers, empty when nothing is selected.</summary>
    public string SelectedText()
    {
        lock (bufferLock)
        {
            return TextSelection.ExtractText(parser.ActiveBuffer, Selection.Current);
        }
    }

    /// <summary>A copy of the visible screen, taken under the lock so rendering the frame
    /// never blocks the pty read thread.</summary>
    public ScreenBuffer Snapshot(out bool cursorVisible)
    {
        lock (bufferLock)
        {
            cursorVisible = parser.Modes.CursorVisible;
            return parser.ActiveBuffer.Snapshot();
        }
    }

    /// <summary>The grid cell under a pixel position, clamped to the screen. Public because
    /// mouse reports need the same pixel-to-cell conversion the selection uses.</summary>
    public (int col, int row) CellAt(Point position)
    {
        var active = parser.ActiveBuffer;
        var col = Math.Clamp((int)(position.X / renderer.cellWidth), 0, active.columns - 1);
        var row = Math.Clamp((int)(position.Y / renderer.cellHeight), 0, active.rows - 1);
        return (col, row);
    }
}
