namespace Centaur.Core.Terminal;

/// <summary>
/// The rectangular cell array a screen is drawn on: its geometry, bounds-safe indexing, and the
/// whole-row moves that scrolling, resizing and snapshotting are built from. Owning the storage
/// here is what lets <see cref="ScreenBuffer"/> and <see cref="ScrollRegion"/> both work on the
/// same cells without either having to reach into the other.
/// </summary>
public sealed class CellGrid
{
    Cell[] cells;

    public int Columns { get; private set; }
    public int Rows { get; private set; }

    /// <summary>The cell an untouched position holds, and what clearing fills with.</summary>
    public Cell Blank { get; }

    public CellGrid(int columns, int rows, Cell blank)
    {
        Columns = columns;
        Rows = rows;
        Blank = blank;
        cells = new Cell[columns * rows];
        Array.Fill(cells, blank);
    }

    /// <summary>Reads outside the grid give <see cref="Blank"/>; writes outside are dropped.</summary>
    public Cell this[int x, int y]
    {
        get => Contains(x, y) ? cells[y * Columns + x] : Blank;
        set
        {
            if (Contains(x, y))
            {
                cells[y * Columns + x] = value;
            }
        }
    }

    public ReadOnlySpan<Cell> Row(int y) => cells.AsSpan(y * Columns, Columns);

    /// <summary>Blanks the whole grid.</summary>
    public void Clear() => Array.Fill(cells, Blank);

    /// <summary>Blanks <paramref name="count"/> rows starting at <paramref name="fromRow"/>.</summary>
    public void ClearRows(int fromRow, int count) =>
        Array.Fill(cells, Blank, fromRow * Columns, count * Columns);

    /// <summary>Copies one row over another within the grid.</summary>
    public void MoveRow(int fromRow, int toRow) =>
        Array.Copy(cells, fromRow * Columns, cells, toRow * Columns, Columns);

    /// <summary>Copies whole rows into <paramref name="target"/>, which must be the same width.</summary>
    public void CopyRowsTo(CellGrid target, int fromRow, int targetRow, int count) =>
        Array.Copy(
            cells,
            fromRow * Columns,
            target.cells,
            targetRow * target.Columns,
            count * Columns
        );

    /// <summary>Writes one row, padding with <see cref="Blank"/> when the source is short and
    /// truncating when it is long - a scrollback line can be either after a resize.</summary>
    public void WriteRow(int row, ReadOnlySpan<Cell> source)
    {
        var copied = Math.Min(source.Length, Columns);
        source[..copied].CopyTo(cells.AsSpan(row * Columns, copied));
        if (copied < Columns)
        {
            Array.Fill(cells, Blank, row * Columns + copied, Columns - copied);
        }
    }

    /// <summary>Grows or shrinks the grid, keeping the content that still fits at the top left.</summary>
    public void Resize(int newColumns, int newRows)
    {
        var resized = new Cell[newColumns * newRows];
        Array.Fill(resized, Blank);

        var copyRows = Math.Min(Rows, newRows);
        var copyColumns = Math.Min(Columns, newColumns);
        for (int y = 0; y < copyRows; y++)
        {
            Array.Copy(cells, y * Columns, resized, y * newColumns, copyColumns);
        }

        cells = resized;
        Columns = newColumns;
        Rows = newRows;
    }

    bool Contains(int x, int y) => x >= 0 && x < Columns && y >= 0 && y < Rows;
}
