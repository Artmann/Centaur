namespace Centaur.Core.Terminal;

public record Cell(char character = ' ', uint foreground = 0xFFFFFFFF, uint background = 0xFF000000);

public class ScreenBuffer
{
    public int columns { get; }
    public int rows { get; }
    public int cursorX { get; set; }
    public int cursorY { get; set; }

    readonly Cell[] cells;
    static readonly Cell defaultCell = new();

    public ScreenBuffer(int columns, int rows)
    {
        this.columns = columns;
        this.rows = rows;

        cells = new Cell[columns * rows];

        Clear();
    }

    public Cell this[int x, int y]
    {
        get => (x >= 0 && x < columns && y >= 0 && y < rows) ? cells[y * columns + x] : defaultCell;
        set
        {
            if (x >= 0 && x < columns && y >= 0 && y < rows)
                cells[y * columns + x] = value;
        }
    }

    public void Clear()
    {
        Array.Fill(cells, defaultCell);
        cursorX = 0;
        cursorY = 0;
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
            ScrollUp();
            cursorY = rows - 1;
        }
        this[cursorX, cursorY] = new Cell(c);
        cursorX++;
    }

    void ScrollUp()
    {
        Array.Copy(cells, columns, cells, 0, columns * (rows - 1));
        Array.Fill(cells, defaultCell, columns * (rows - 1), columns);
    }
}
