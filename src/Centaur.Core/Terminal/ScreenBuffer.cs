namespace Centaur.Core.Terminal;

public record Cell(
    char character = ' ',
    uint foreground = 0xFFFFFFFF,
    uint background = 0xFF000000
);

public class ScreenBuffer
{
    public int columns { get; }
    public int rows { get; }
    public int cursorX { get; set; }
    public int cursorY { get; set; }

    readonly Cell[] cells;
    readonly Cell defaultCell;

    public ScreenBuffer(int columns, int rows)
        : this(columns, rows, CatppuccinThemes.Macchiato) { }

    public ScreenBuffer(int columns, int rows, TerminalTheme theme)
    {
        this.columns = columns;
        this.rows = rows;
        defaultCell = new Cell(' ', theme.Foreground, theme.Background);

        cells = new Cell[columns * rows];

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
            ScrollUp(1);
            cursorY = rows - 1;
        }
        this[cursorX, cursorY] = new Cell(c, defaultCell.foreground, defaultCell.background);
        cursorX++;
    }

    public void ScrollUp(int lines = 1)
    {
        if (lines <= 0)
            return;
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
            return;
        if (lines >= rows)
        {
            Array.Fill(cells, defaultCell);
            return;
        }
        Array.Copy(cells, 0, cells, columns * lines, columns * (rows - lines));
        Array.Fill(cells, defaultCell, 0, columns * lines);
    }
}
