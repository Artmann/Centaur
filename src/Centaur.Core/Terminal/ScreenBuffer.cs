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
    readonly CellGrid grid;

    // Pre-allocated snapshot buffer for lock-free rendering
    ScreenBuffer? snapshotBuffer;

    public int columns => grid.Columns;
    public int rows => grid.Rows;
    public int cursorX { get; set; }
    public int cursorY { get; set; }

    /// <summary>The DECSTBM margins, and the row moves bounded by them.</summary>
    public ScrollRegion Region { get; }

    /// <summary>Rows that have scrolled off the top, and how far back the view is parked.</summary>
    public ScrollbackBuffer Scrollback { get; }

    /// <summary>Semantic-prompt mark per row (OSC 133).</summary>
    public PromptMarks Marks { get; }

    public ScreenBuffer(
        int columns,
        int rows,
        TerminalTheme? theme = null,
        bool enableScrollback = true
    )
        : this(
            columns,
            rows,
            new Cell(
                ' ',
                (theme ?? CatppuccinThemes.Macchiato).Foreground,
                (theme ?? CatppuccinThemes.Macchiato).Background
            ),
            enableScrollback
        ) { }

    ScreenBuffer(int columns, int rows, Cell blank, bool enableScrollback)
    {
        grid = new CellGrid(columns, rows, blank);
        Scrollback = new ScrollbackBuffer(enableScrollback ? 10000 : 0);
        Marks = new PromptMarks(rows);
        Region = new ScrollRegion(grid, Scrollback);
    }

    public Cell this[int x, int y]
    {
        get => grid[x, y];
        set => grid[x, y] = value;
    }

    public ReadOnlySpan<Cell> GetRow(int y) => grid.Row(y);

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
            snapshotBuffer = new ScreenBuffer(columns, rows, grid.Blank, enableScrollback: false);
        }

        if (Scrollback.Offset > 0)
        {
            CopyScrolledBackView(snapshotBuffer);
        }
        else
        {
            grid.CopyRowsTo(snapshotBuffer.grid, 0, 0, rows);
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

        // A history row can be narrower than the viewport is now, if the window grew - WriteRow
        // pads the rest with blanks.
        for (int y = 0; y < scrollbackRows; y++)
        {
            target.grid.WriteRow(y, Scrollback.GetLine(scrollbackStart + y));
        }

        if (liveRows > 0)
        {
            grid.CopyRowsTo(target.grid, 0, scrollbackRows, liveRows);
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

        var copyRows = Math.Min(rows, newRows);
        grid.Resize(newColumns, newRows);
        Marks.Resize(newRows, copyRows);

        Scrollback.ScrollToBottom();
        snapshotBuffer = null; // Force re-creation on next snapshot

        // Clamp cursor
        cursorX = Math.Clamp(cursorX, 0, newColumns - 1);
        cursorY = Math.Clamp(cursorY, 0, newRows - 1);

        Region.Reset();
    }

    public void Clear()
    {
        grid.Clear();
        cursorX = 0;
        cursorY = 0;
    }

    public void ClearCells()
    {
        grid.Clear();
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
            Region.ScrollUp(1);
            cursorY = rows - 1;
        }
        this[cursorX, cursorY] = new Cell(c, grid.Blank.foreground, grid.Blank.background);
        cursorX++;
    }
}
