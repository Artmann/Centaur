namespace Centaur.Core.Terminal;

/// <summary>
/// The scrolling half of a <see cref="ScreenBuffer"/>: the DECSTBM margins a program can set,
/// and every move of rows within them.
/// </summary>
public sealed class ScrollRegion
{
    readonly CellGrid grid;
    readonly ScrollbackBuffer scrollback;

    internal ScrollRegion(CellGrid grid, ScrollbackBuffer scrollback)
    {
        this.grid = grid;
        this.scrollback = scrollback;
        Bottom = grid.Rows - 1;
    }

    /// <summary>First row of the region, 0-based.</summary>
    public int Top { get; private set; }

    /// <summary>Last row of the region, inclusive.</summary>
    public int Bottom { get; private set; }

    /// <summary>Narrows the region (DECSTBM). An inverted or empty range resets it to the
    /// whole screen, which is what the sequence means when it carries no useful arguments.</summary>
    public void Set(int top, int bottom)
    {
        top = Math.Clamp(top, 0, grid.Rows - 1);
        bottom = Math.Clamp(bottom, 0, grid.Rows - 1);
        if (top >= bottom)
        {
            Reset();
        }
        else
        {
            Top = top;
            Bottom = bottom;
        }
    }

    /// <summary>Widens the region back out to the whole screen.</summary>
    public void Reset()
    {
        Top = 0;
        Bottom = grid.Rows - 1;
    }

    /// <summary>Scrolls the whole screen, which is the region no program has narrowed.</summary>
    public void ScrollUp(int lines = 1) => ScrollUpIn(lines, 0, grid.Rows - 1);

    public void ScrollDown(int lines = 1) => ScrollDownIn(lines, 0, grid.Rows - 1);

    public void ScrollUpIn(int lines, int top, int bottom)
    {
        if (lines <= 0)
        {
            return;
        }

        var regionHeight = bottom - top + 1;

        // Rows only reach scrollback when they leave the screen entirely. Inside a smaller
        // scroll region they are being reused by the program that set the region, not
        // scrolled away, so they are discarded.
        if (top == 0 && bottom == grid.Rows - 1)
        {
            PushRowsToScrollback(top, Math.Min(lines, regionHeight));
        }

        if (lines >= regionHeight)
        {
            grid.ClearRows(top, regionHeight);
            return;
        }

        for (int y = top; y <= bottom - lines; y++)
        {
            grid.MoveRow(y + lines, y);
        }

        grid.ClearRows(bottom - lines + 1, lines);
    }

    public void ScrollDownIn(int lines, int top, int bottom)
    {
        if (lines <= 0)
        {
            return;
        }

        var regionHeight = bottom - top + 1;
        if (lines >= regionHeight)
        {
            grid.ClearRows(top, regionHeight);
            return;
        }

        for (int y = bottom; y >= top + lines; y--)
        {
            grid.MoveRow(y - lines, y);
        }

        grid.ClearRows(top, lines);
    }

    /// <summary>Hands <paramref name="count"/> rows starting at <paramref name="fromRow"/> to
    /// the scrollback, which ignores them when the screen keeps no history.</summary>
    void PushRowsToScrollback(int fromRow, int count)
    {
        for (int y = fromRow; y < fromRow + count; y++)
        {
            scrollback.PushLine(grid.Row(y));
        }
    }
}
