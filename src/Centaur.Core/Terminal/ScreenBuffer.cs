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

    Cell[] cells;
    readonly Cell defaultCell;

    // Pre-allocated snapshot buffer for lock-free rendering
    ScreenBuffer? snapshotBuffer;

    public ScreenBuffer(int columns, int rows)
        : this(columns, rows, CatppuccinThemes.Macchiato) { }

    public ScreenBuffer(int columns, int rows, TerminalTheme theme)
    {
        this.columns = columns;
        this.rows = rows;
        defaultCell = new Cell(' ', theme.Foreground, theme.Background);

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
        snapshotBuffer ??= new ScreenBuffer(columns, rows);
        Array.Copy(cells, snapshotBuffer.cells, cells.Length);
        snapshotBuffer.cursorX = cursorX;
        snapshotBuffer.cursorY = cursorY;
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
            Array.Fill(cells, defaultCell);
            return;
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
        var regionHeight = bottom - top + 1;
        if (lines >= regionHeight)
        {
            for (int y = top; y <= bottom; y++)
            {
                Array.Fill(cells, defaultCell, y * columns, columns);
            }
            return;
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
}
