using System.Text;
using Centaur.Core.Terminal;

namespace Centaur.Rendering;

public readonly record struct TextSelection(
    int StartColumn,
    int StartRow,
    int EndColumn,
    int EndRow
)
{
    public static TextSelection Normalize(
        int anchorCol,
        int anchorRow,
        int currentCol,
        int currentRow
    )
    {
        if (anchorRow > currentRow || (anchorRow == currentRow && anchorCol > currentCol))
        {
            return new TextSelection(currentCol, currentRow, anchorCol, anchorRow);
        }
        return new TextSelection(anchorCol, anchorRow, currentCol, currentRow);
    }

    public static bool IsInSelection(int x, int y, TextSelection sel)
    {
        if (y < sel.StartRow || y > sel.EndRow)
        {
            return false;
        }
        if (sel.StartRow == sel.EndRow)
        {
            return x >= sel.StartColumn && x < sel.EndColumn;
        }
        if (y == sel.StartRow)
        {
            return x >= sel.StartColumn;
        }
        if (y == sel.EndRow)
        {
            return x < sel.EndColumn;
        }
        return true;
    }

    static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    public static int FindWordStart(ScreenBuffer buffer, int col, int row)
    {
        bool wordChar = IsWordChar(buffer[col, row].character);
        while (col > 0 && IsWordChar(buffer[col - 1, row].character) == wordChar)
        {
            col--;
        }
        return col;
    }

    public static int FindWordEnd(ScreenBuffer buffer, int col, int row)
    {
        bool wordChar = IsWordChar(buffer[col, row].character);
        while (col < buffer.columns - 1 && IsWordChar(buffer[col + 1, row].character) == wordChar)
        {
            col++;
        }
        return col + 1;
    }

    public static string ExtractText(ScreenBuffer buffer, TextSelection sel)
    {
        var sb = new StringBuilder();
        for (int y = sel.StartRow; y <= sel.EndRow; y++)
        {
            int startX = (y == sel.StartRow) ? sel.StartColumn : 0;
            int endX = (y == sel.EndRow) ? sel.EndColumn : buffer.columns;

            for (int x = startX; x < endX; x++)
            {
                sb.Append(buffer[x, y].character);
            }

            if (y < sel.EndRow)
            {
                // Trim trailing spaces from this line
                while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                {
                    sb.Length--;
                }
                sb.Append('\n');
            }
        }

        // Trim trailing spaces on last line
        while (sb.Length > 0 && sb[sb.Length - 1] == ' ')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
