namespace Centaur.Core.Terminal;

/// <summary>Semantic-prompt marks (OSC 133), one per screen row. Out-of-range rows read as
/// <see cref="PromptMark.None"/> and ignore writes, so callers never have to bounds-check.</summary>
public sealed class PromptMarks
{
    PromptMark[] marks;

    internal PromptMarks(int rows)
    {
        marks = new PromptMark[rows];
    }

    public PromptMark this[int row]
    {
        get => row >= 0 && row < marks.Length ? marks[row] : PromptMark.None;
        set
        {
            if (row >= 0 && row < marks.Length)
            {
                marks[row] = value;
            }
        }
    }

    /// <summary>Keeps the marks of the surviving rows, mirroring the cell copy a resize does.</summary>
    internal void Resize(int newRows, int copyRows)
    {
        var resized = new PromptMark[newRows];
        Array.Copy(marks, resized, copyRows);
        marks = resized;
    }
}
