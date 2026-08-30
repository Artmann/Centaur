namespace Centaur.Core.Terminal;

/// <summary>
/// Screen-editing primitives shared by the escape-sequence dispatchers: printing at the cursor,
/// line feeds inside the scroll region, and the erase/insert/delete family. Stateless — every
/// operation is a function of the buffer, the cursor it carries, and the blank cell to fill with.
/// </summary>
static class ScreenOps
{
    /// <summary>Prints <paramref name="cell"/> at the cursor, wrapping to the next line first
    /// when the cursor has run past the last column.</summary>
    public static void Write(ScreenBuffer buffer, Cell cell)
    {
        if (buffer.cursorX >= buffer.columns)
        {
            buffer.cursorX = 0;
            LineFeed(buffer);
        }
        buffer[buffer.cursorX, buffer.cursorY] = cell;
        buffer.cursorX++;
    }

    /// <summary>Moves down one row, scrolling the region when the cursor is on its last row.
    /// Any scrollback the user had scrolled into snaps back to the live edge.</summary>
    public static void LineFeed(ScreenBuffer buffer)
    {
        if (buffer.Scrollback.Offset > 0)
        {
            buffer.Scrollback.ScrollToBottom();
        }

        if (buffer.cursorY == buffer.Region.Bottom)
        {
            buffer.Region.ScrollUpIn(1, buffer.Region.Top, buffer.Region.Bottom);
        }
        else if (buffer.cursorY < buffer.rows - 1)
        {
            buffer.cursorY++;
        }
    }

    /// <summary>RI: moves up one row, scrolling the region down when the cursor is already on
    /// its top row.</summary>
    public static void ReverseIndex(ScreenBuffer buffer)
    {
        if (buffer.cursorY == buffer.Region.Top)
        {
            buffer.Region.ScrollDownIn(1, buffer.Region.Top, buffer.Region.Bottom);
        }
        else if (buffer.cursorY > 0)
        {
            buffer.cursorY--;
        }
    }

    public static void EraseInDisplay(ScreenBuffer buffer, int mode, Cell blank)
    {
        switch (mode)
        {
            case 0: // Erase from cursor to end of screen
                EraseInLine(buffer, 0, blank);
                FillRows(buffer, buffer.cursorY + 1, buffer.rows - 1, blank);
                break;
            case 1: // Erase from start of screen to cursor
                FillRows(buffer, 0, buffer.cursorY - 1, blank);
                EraseInLine(buffer, 1, blank);
                break;
            case 2: // Erase entire screen (preserve cursor)
                buffer.ClearCells();
                break;
            case 3: // Erase entire screen and scrollback
                buffer.ClearCells();
                buffer.Scrollback.Clear();
                break;
        }
    }

    public static void EraseInLine(ScreenBuffer buffer, int mode, Cell blank)
    {
        switch (mode)
        {
            case 0: // Erase from cursor to end of line
                FillRow(buffer, buffer.cursorY, buffer.cursorX, buffer.columns - 1, blank);
                break;
            case 1: // Erase from start of line to cursor
                FillRow(buffer, buffer.cursorY, 0, buffer.cursorX, blank);
                break;
            case 2: // Erase entire line
                FillRow(buffer, buffer.cursorY, 0, buffer.columns - 1, blank);
                break;
        }
    }

    /// <summary>Opens <paramref name="count"/> blank lines at the cursor row, pushing the rows
    /// below it down and off the bottom of the screen.</summary>
    public static void InsertLines(ScreenBuffer buffer, int count, Cell blank)
    {
        for (int i = 0; i < count && buffer.cursorY + i < buffer.rows; i++)
        {
            for (int y = buffer.rows - 1; y > buffer.cursorY; y--)
            {
                CopyRow(buffer, from: y - 1, to: y);
            }
            FillRow(buffer, buffer.cursorY, 0, buffer.columns - 1, blank);
        }
    }

    /// <summary>Removes <paramref name="count"/> lines at the cursor row, pulling the rows below
    /// it up and blanking the bottom row for each one removed.</summary>
    public static void DeleteLines(ScreenBuffer buffer, int count, Cell blank)
    {
        for (int i = 0; i < count && buffer.cursorY + i < buffer.rows; i++)
        {
            for (int y = buffer.cursorY; y < buffer.rows - 1; y++)
            {
                CopyRow(buffer, from: y + 1, to: y);
            }
            FillRow(buffer, buffer.rows - 1, 0, buffer.columns - 1, blank);
        }
    }

    /// <summary>Deletes <paramref name="count"/> characters at the cursor, shifting the rest of
    /// the line left and blanking the tail.</summary>
    public static void DeleteCharacters(ScreenBuffer buffer, int count, Cell blank)
    {
        var y = buffer.cursorY;
        for (int i = 0; i < count; i++)
        {
            for (int x = buffer.cursorX; x < buffer.columns - 1; x++)
            {
                buffer[x, y] = buffer[x + 1, y];
            }
            buffer[buffer.columns - 1, y] = blank;
        }
    }

    /// <summary>Opens <paramref name="count"/> blank cells at the cursor, shifting the rest of
    /// the line right and off the end.</summary>
    public static void InsertCharacters(ScreenBuffer buffer, int count, Cell blank)
    {
        var y = buffer.cursorY;
        for (int x = buffer.columns - 1; x >= buffer.cursorX + count; x--)
        {
            buffer[x, y] = buffer[x - count, y];
        }
        EraseCharacters(buffer, count, blank);
    }

    /// <summary>Blanks <paramref name="count"/> cells at the cursor without shifting the line.</summary>
    public static void EraseCharacters(ScreenBuffer buffer, int count, Cell blank)
    {
        var last = Math.Min(buffer.cursorX + count, buffer.columns) - 1;
        FillRow(buffer, buffer.cursorY, buffer.cursorX, last, blank);
    }

    static void FillRows(ScreenBuffer buffer, int fromRow, int toRow, Cell blank)
    {
        for (int y = fromRow; y <= toRow; y++)
        {
            FillRow(buffer, y, 0, buffer.columns - 1, blank);
        }
    }

    static void FillRow(ScreenBuffer buffer, int row, int fromColumn, int toColumn, Cell blank)
    {
        for (int x = fromColumn; x <= toColumn; x++)
        {
            buffer[x, row] = blank;
        }
    }

    static void CopyRow(ScreenBuffer buffer, int from, int to)
    {
        for (int x = 0; x < buffer.columns; x++)
        {
            buffer[x, to] = buffer[x, from];
        }
    }
}
