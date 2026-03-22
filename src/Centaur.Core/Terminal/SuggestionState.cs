namespace Centaur.Core.Terminal;

public class SuggestionState
{
    readonly object syncLock = new();
    string? ghostText;
    int cursorCol;
    int cursorRow;

    public void Update(string? text, int col, int row)
    {
        lock (syncLock)
        {
            ghostText = text;
            cursorCol = col;
            cursorRow = row;
        }
    }

    public void Clear()
    {
        lock (syncLock)
        {
            ghostText = null;
        }
    }

    public (string? text, int col, int row) Read()
    {
        lock (syncLock)
        {
            return (ghostText, cursorCol, cursorRow);
        }
    }
}
