namespace Centaur.Core.Terminal;

public record Cell(char character = ' ', uint foreground = 0xFFFFFFFF, uint background = 0xFF000000)
{
    public bool bold { get; init; }
    public bool faint { get; init; }
    public bool italic { get; init; }
    public UnderlineStyle underline { get; init; }

    // Underline color as ARGB; 0 is the sentinel meaning "inherit the foreground".
    public uint underlineColor { get; init; }
    public bool blink { get; init; }
    public bool inverse { get; init; }
    public bool invisible { get; init; }
    public bool strikethrough { get; init; }
    public bool overline { get; init; }

    // Active OSC 8 hyperlink target, or null when the cell is not part of a link.
    public string? hyperlink { get; init; }
}

public class ScreenBuffer
{
    public int columns { get; private set; }
    public int rows { get; private set; }
    public int cursorX { get; set; }
    public int cursorY { get; set; }
    public int scrollTop { get; private set; }
    public int scrollBottom { get; private set; }

    /// <summary>Rows that have scrolled off the top, and how far back the view is parked.</summary>
    public ScrollbackBuffer Scrollback { get; }

    Cell[] cells;

    // Semantic-prompt mark per row (OSC 133). Indexed by row.
    PromptMark[] marks;
    readonly Cell defaultCell;

    // Pre-allocated snapshot buffer for lock-free rendering
    ScreenBuffer? snapshotBuffer;

    public ScreenBuffer(
        int columns,
        int rows,
        TerminalTheme? theme = null,
        bool enableScrollback = true
    )
    {
        this.columns = columns;
        this.rows = rows;
        theme ??= CatppuccinThemes.Macchiato;
        defaultCell = new Cell(' ', theme.Foreground, theme.Background);
        Scrollback = new ScrollbackBuffer(enableScrollback ? 10000 : 0);

        cells = new Cell[columns * rows];
        marks = new PromptMark[rows];
        scrollTop = 0;
        scrollBottom = rows - 1;

        Clear();
    }

    public Cell this[int x, int y]
    {
        get => (x >= 0 && x < columns && y >= 0 && y < rows) ? cells[y * columns + x] : defaultCell;
        set
        {
            if (x >= 0 && x < columns && y >= 0 && y < rows)
            {
                cells[y * columns + x] = value;
            }
        }
    }

    public ReadOnlySpan<Cell> GetRow(int y) => cells.AsSpan(y * columns, columns);

    public PromptMark GetMark(int row) =>
        row >= 0 && row < marks.Length ? marks[row] : PromptMark.None;

    public void SetMark(int row, PromptMark mark)
    {
        if (row >= 0 && row < marks.Length)
        {
            marks[row] = mark;
        }
    }

    /// <summary>
    /// A stable copy of what should be on screen right now, for the renderer to walk without
    /// holding the parse lock. The same buffer is handed back every frame - callers render it
    /// and drop it, they never keep it.
    /// </summary>
    public ScreenBuffer Snapshot()
    {
        if (
            snapshotBuffer == null
            || snapshotBuffer.columns != columns
            || snapshotBuffer.rows != rows
        )
        {
            snapshotBuffer = new ScreenBuffer(columns, rows, enableScrollback: false);
        }

        if (Scrollback.Offset > 0)
        {
            CopyScrolledBackView(snapshotBuffer);
        }
        else
        {
            Array.Copy(cells, snapshotBuffer.cells, cells.Length);
            snapshotBuffer.cursorX = cursorX;
            snapshotBuffer.cursorY = cursorY;
        }

        return snapshotBuffer;
    }

    /// <summary>
    /// Scrolled up: the view straddles the scrollback and the live grid, so the top rows come
    /// from history and whatever is left of the viewport comes from the live buffer.
    /// </summary>
    void CopyScrolledBackView(ScreenBuffer target)
    {
        var scrollbackRows = Math.Min(Scrollback.Offset, rows);
        var liveRows = rows - scrollbackRows;
        var scrollbackStart = Scrollback.Count - Scrollback.Offset;

        for (int y = 0; y < scrollbackRows; y++)
        {
            var line = Scrollback.GetLine(scrollbackStart + y);
            var copyLen = Math.Min(line.Length, columns);
            Array.Copy(line, 0, target.cells, y * columns, copyLen);

            // A history row can be narrower than the viewport is now, if the window grew.
            if (copyLen < columns)
            {
                Array.Fill(target.cells, defaultCell, y * columns + copyLen, columns - copyLen);
            }
        }

        if (liveRows > 0)
        {
            Array.Copy(cells, 0, target.cells, scrollbackRows * columns, liveRows * columns);
        }

        target.cursorX = cursorX;
        target.cursorY = -1; // Hide the cursor while the view is off the live edge.
    }

    public void Resize(int newColumns, int newRows)
    {
        if (newColumns == columns && newRows == rows)
        {
            return;
        }

        var newCells = new Cell[newColumns * newRows];
        Array.Fill(newCells, defaultCell);

        // Copy existing content that fits
        var copyRows = Math.Min(rows, newRows);
        var copyCols = Math.Min(columns, newColumns);
        for (int y = 0; y < copyRows; y++)
        {
            for (int x = 0; x < copyCols; x++)
            {
                newCells[y * newColumns + x] = cells[y * columns + x];
            }
        }

        // Preserve semantic-prompt marks for surviving rows, mirroring the cell copy.
        var newMarks = new PromptMark[newRows];
        Array.Copy(marks, newMarks, copyRows);

        cells = newCells;
        marks = newMarks;
        columns = newColumns;
        rows = newRows;
        Scrollback.ScrollToBottom();
        snapshotBuffer = null; // Force re-creation on next snapshot

        // Clamp cursor
        cursorX = Math.Clamp(cursorX, 0, newColumns - 1);
        cursorY = Math.Clamp(cursorY, 0, newRows - 1);

        // Reset scroll region to full screen
        scrollTop = 0;
        scrollBottom = newRows - 1;
    }

    public void SetScrollRegion(int top, int bottom)
    {
        top = Math.Clamp(top, 0, rows - 1);
        bottom = Math.Clamp(bottom, 0, rows - 1);
        if (top >= bottom)
        {
            scrollTop = 0;
            scrollBottom = rows - 1;
        }
        else
        {
            scrollTop = top;
            scrollBottom = bottom;
        }
    }

    public void Clear()
    {
        Array.Fill(cells, defaultCell);
        cursorX = 0;
        cursorY = 0;
    }

    public void ClearCells()
    {
        Array.Fill(cells, defaultCell);
    }

    public void Write(char c)
    {
        if (cursorX >= columns)
        {
            cursorX = 0;
            cursorY++;
        }
        if (cursorY >= rows)
        {
            ScrollUp(1);
            cursorY = rows - 1;
        }
        this[cursorX, cursorY] = new Cell(c, defaultCell.foreground, defaultCell.background);
        cursorX++;
    }

    /// <summary>Scrolls the whole screen, which is the scroll region no program has narrowed.</summary>
    public void ScrollUp(int lines = 1) => ScrollUpInRegion(lines, 0, rows - 1);

    public void ScrollDown(int lines = 1) => ScrollDownInRegion(lines, 0, rows - 1);

    public void ScrollUpInRegion(int lines, int top, int bottom)
    {
        if (lines <= 0)
        {
            return;
        }

        var regionHeight = bottom - top + 1;

        // Rows only reach scrollback when they leave the screen entirely. Inside a smaller
        // scroll region they are being reused by the program that set the region, not
        // scrolled away, so they are discarded.
        if (top == 0 && bottom == rows - 1)
        {
            PushRowsToScrollback(top, Math.Min(lines, regionHeight));
        }

        if (lines >= regionHeight)
        {
            ClearRows(top, regionHeight);
            return;
        }

        for (int y = top; y <= bottom - lines; y++)
        {
            Array.Copy(cells, (y + lines) * columns, cells, y * columns, columns);
        }

        ClearRows(bottom - lines + 1, lines);
    }

    public void ScrollDownInRegion(int lines, int top, int bottom)
    {
        if (lines <= 0)
        {
            return;
        }
        var regionHeight = bottom - top + 1;
        if (lines >= regionHeight)
        {
            ClearRows(top, regionHeight);
            return;
        }

        for (int y = bottom; y >= top + lines; y--)
        {
            Array.Copy(cells, (y - lines) * columns, cells, y * columns, columns);
        }

        ClearRows(top, lines);
    }

    /// <summary>Hands <paramref name="count"/> rows starting at <paramref name="fromRow"/> to
    /// the scrollback, which ignores them when this buffer keeps no history.</summary>
    void PushRowsToScrollback(int fromRow, int count)
    {
        for (int y = fromRow; y < fromRow + count; y++)
        {
            Scrollback.PushLine(cells.AsSpan(y * columns, columns));
        }
    }

    void ClearRows(int fromRow, int count)
    {
        Array.Fill(cells, defaultCell, fromRow * columns, count * columns);
    }
}
