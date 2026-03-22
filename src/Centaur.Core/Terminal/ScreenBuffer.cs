namespace Centaur.Core.Terminal;

public record Cell(
    char character = ' ',
    uint foreground = 0xFFFFFFFF,
    uint background = 0xFF000000
);

public class ScreenBuffer
{
    public int columns { get; private set; }
    public int rows { get; private set; }
    public int cursorX { get; set; }
    public int cursorY { get; set; }
    public int scrollTop { get; private set; }
    public int scrollBottom { get; private set; }
    public int ScrollOffset { get; private set; }
    public int ScrollbackCount => scrollback?.Count ?? 0;

    Cell[] cells;
    readonly Cell defaultCell;
    readonly ScrollbackBuffer? scrollback;

    // Pre-allocated snapshot buffer for lock-free rendering
    ScreenBuffer? snapshotBuffer;

    public ScreenBuffer(int columns, int rows)
        : this(columns, rows, CatppuccinThemes.Macchiato) { }

    public ScreenBuffer(int columns, int rows, TerminalTheme theme)
        : this(columns, rows, theme, enableScrollback: true) { }

    public ScreenBuffer(int columns, int rows, bool enableScrollback)
        : this(columns, rows, CatppuccinThemes.Macchiato, enableScrollback) { }

    public ScreenBuffer(int columns, int rows, TerminalTheme theme, bool enableScrollback)
    {
        this.columns = columns;
        this.rows = rows;
        defaultCell = new Cell(' ', theme.Foreground, theme.Background);

        if (enableScrollback)
        {
            scrollback = new ScrollbackBuffer(10000);
        }

        cells = new Cell[columns * rows];
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

        if (ScrollOffset > 0 && scrollback != null)
        {
            var scrollbackRows = Math.Min(ScrollOffset, rows);
            var liveRows = rows - scrollbackRows;
            var scrollbackStart = scrollback.Count - ScrollOffset;

            // Copy scrollback rows into top of snapshot
            for (int y = 0; y < scrollbackRows; y++)
            {
                var line = scrollback.GetLine(scrollbackStart + y);
                var copyLen = Math.Min(line.Length, columns);
                Array.Copy(line, 0, snapshotBuffer.cells, y * columns, copyLen);
                // Pad if scrollback row is narrower than current viewport
                if (copyLen < columns)
                {
                    Array.Fill(
                        snapshotBuffer.cells,
                        defaultCell,
                        y * columns + copyLen,
                        columns - copyLen
                    );
                }
            }

            // Copy live buffer rows into bottom of snapshot
            if (liveRows > 0)
            {
                Array.Copy(
                    cells,
                    0,
                    snapshotBuffer.cells,
                    scrollbackRows * columns,
                    liveRows * columns
                );
            }

            snapshotBuffer.cursorX = cursorX;
            snapshotBuffer.cursorY = -1; // Hide cursor when scrolled up
        }
        else
        {
            Array.Copy(cells, snapshotBuffer.cells, cells.Length);
            snapshotBuffer.cursorX = cursorX;
            snapshotBuffer.cursorY = cursorY;
        }

        return snapshotBuffer;
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

        cells = newCells;
        columns = newColumns;
        rows = newRows;
        ScrollOffset = 0;
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

    public void ScrollUp(int lines = 1)
    {
        if (lines <= 0)
        {
            return;
        }
        if (lines >= rows)
        {
            // Capture all rows to scrollback before clearing
            if (scrollback != null)
            {
                for (int y = 0; y < rows; y++)
                {
                    scrollback.PushLine(cells.AsSpan(y * columns, columns));
                }
            }
            Array.Fill(cells, defaultCell);
            return;
        }
        // Capture the rows about to scroll off the top
        if (scrollback != null)
        {
            for (int y = 0; y < lines; y++)
            {
                scrollback.PushLine(cells.AsSpan(y * columns, columns));
            }
        }
        Array.Copy(cells, columns * lines, cells, 0, columns * (rows - lines));
        Array.Fill(cells, defaultCell, columns * (rows - lines), columns * lines);
    }

    public void ScrollDown(int lines = 1)
    {
        if (lines <= 0)
        {
            return;
        }
        if (lines >= rows)
        {
            Array.Fill(cells, defaultCell);
            return;
        }
        Array.Copy(cells, 0, cells, columns * lines, columns * (rows - lines));
        Array.Fill(cells, defaultCell, 0, columns * lines);
    }

    public void ScrollUpInRegion(int lines, int top, int bottom)
    {
        if (lines <= 0)
        {
            return;
        }

        var isFullScreen = top == 0 && bottom == rows - 1;
        var regionHeight = bottom - top + 1;

        if (lines >= regionHeight)
        {
            // Capture rows to scrollback if full-screen region
            if (isFullScreen && scrollback != null)
            {
                for (int y = top; y <= bottom; y++)
                {
                    scrollback.PushLine(cells.AsSpan(y * columns, columns));
                }
            }
            for (int y = top; y <= bottom; y++)
            {
                Array.Fill(cells, defaultCell, y * columns, columns);
            }
            return;
        }

        // Capture the rows about to scroll off the top (only for full-screen region)
        if (isFullScreen && scrollback != null)
        {
            for (int y = top; y < top + lines; y++)
            {
                scrollback.PushLine(cells.AsSpan(y * columns, columns));
            }
        }

        // Shift lines up within region
        for (int y = top; y <= bottom - lines; y++)
        {
            Array.Copy(cells, (y + lines) * columns, cells, y * columns, columns);
        }
        // Clear bottom lines of region
        for (int y = bottom - lines + 1; y <= bottom; y++)
        {
            Array.Fill(cells, defaultCell, y * columns, columns);
        }
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
            for (int y = top; y <= bottom; y++)
            {
                Array.Fill(cells, defaultCell, y * columns, columns);
            }
            return;
        }
        // Shift lines down within region
        for (int y = bottom; y >= top + lines; y--)
        {
            Array.Copy(cells, (y - lines) * columns, cells, y * columns, columns);
        }
        // Clear top lines of region
        for (int y = top; y < top + lines; y++)
        {
            Array.Fill(cells, defaultCell, y * columns, columns);
        }
    }

    public void ScrollViewUp(int lines)
    {
        if (scrollback == null)
        {
            return;
        }
        ScrollOffset = Math.Min(ScrollOffset + lines, scrollback.Count);
    }

    public void ScrollViewDown(int lines)
    {
        ScrollOffset = Math.Max(ScrollOffset - lines, 0);
    }

    public void ScrollToBottom()
    {
        ScrollOffset = 0;
    }

    public void ClearScrollback()
    {
        scrollback?.Clear();
        ScrollOffset = 0;
    }
}
