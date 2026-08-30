using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// Mouse text selection over the terminal grid: what is currently selected, and how a drag
/// extends it at character, word or line granularity.
///
/// Split out of <see cref="TerminalControl"/> so the grid arithmetic stands on its own; the
/// control keeps the parts that are genuinely Avalonia's - pointer capture, redraws, focus.
/// Word and line snapping read the live grid, so call these with the buffer lock held.
/// </summary>
public sealed class SelectionController
{
    enum Granularity
    {
        Character,
        Word,
        Line,
    }

    int anchorCol,
        anchorRow;
    int currentCol,
        currentRow;

    // Where the double-clicked word started and ended. A word drag pivots around these
    // rather than the clicked cell, so dragging backwards keeps the whole word selected.
    int wordAnchorStart,
        wordAnchorEnd;

    Granularity granularity;

    public bool HasSelection { get; private set; }

    public bool IsDragging { get; private set; }

    /// <summary>The selection with its endpoints in reading order.</summary>
    public TextSelection Current =>
        TextSelection.Normalize(anchorCol, anchorRow, currentCol, currentRow);

    /// <summary>The selection, or null when nothing is selected.</summary>
    public TextSelection? Normalized => HasSelection ? Current : null;

    /// <summary>
    /// Starts a drag at the clicked cell. The click count picks the granularity: one selects
    /// characters, two the word under the cursor, three or more the whole line.
    /// </summary>
    public void BeginDrag(ScreenBuffer buffer, int col, int row, int clickCount)
    {
        if (clickCount >= 3)
        {
            granularity = Granularity.Line;
            anchorCol = 0;
            anchorRow = row;
            currentCol = buffer.columns;
            currentRow = row;
            HasSelection = true;
        }
        else if (clickCount == 2)
        {
            granularity = Granularity.Word;
            wordAnchorStart = TextSelection.FindWordStart(buffer, col, row);
            wordAnchorEnd = TextSelection.FindWordEnd(buffer, col, row);
            anchorCol = wordAnchorStart;
            anchorRow = row;
            currentCol = wordAnchorEnd;
            currentRow = row;
            HasSelection = true;
        }
        else
        {
            granularity = Granularity.Character;
            anchorCol = col;
            anchorRow = row;
            currentCol = col;
            currentRow = row;

            // A plain click is not yet a selection; it becomes one once the pointer moves.
            HasSelection = false;
        }

        IsDragging = true;
    }

    /// <summary>Moves the loose end of the drag to the cell under the pointer.</summary>
    public void ExtendDrag(ScreenBuffer buffer, int col, int row)
    {
        switch (granularity)
        {
            case Granularity.Line:
                ExtendByLine(buffer, row);
                break;
            case Granularity.Word:
                ExtendByWord(buffer, col, row);
                break;
            default:
                ExtendByCharacter(col, row);
                break;
        }
    }

    /// <summary>
    /// Ends the drag. A character drag that never left its starting cell was just a click,
    /// so it clears the selection instead of leaving a zero-width one behind.
    /// </summary>
    public void EndDrag(int col, int row)
    {
        IsDragging = false;

        if (granularity == Granularity.Character && col == anchorCol && row == anchorRow)
        {
            HasSelection = false;
        }
    }

    public void Clear()
    {
        HasSelection = false;
    }

    void ExtendByLine(ScreenBuffer buffer, int row)
    {
        // Dragging above the anchor line flips which end of the line each endpoint sits on,
        // so the run still reads forwards once normalized.
        if (row < anchorRow)
        {
            anchorCol = buffer.columns;
            currentCol = 0;
        }
        else
        {
            anchorCol = 0;
            currentCol = buffer.columns;
        }

        currentRow = row;
        HasSelection = true;
    }

    void ExtendByWord(ScreenBuffer buffer, int col, int row)
    {
        var beforeAnchor = row < anchorRow || (row == anchorRow && col < wordAnchorStart);
        if (beforeAnchor)
        {
            anchorCol = wordAnchorEnd;
            currentCol = TextSelection.FindWordStart(buffer, col, row);
        }
        else
        {
            anchorCol = wordAnchorStart;
            currentCol = TextSelection.FindWordEnd(buffer, col, row);
        }

        currentRow = row;
        HasSelection = true;
    }

    void ExtendByCharacter(int col, int row)
    {
        currentCol = col;
        currentRow = row;

        if (col != anchorCol || row != anchorRow)
        {
            HasSelection = true;
        }
    }
}
