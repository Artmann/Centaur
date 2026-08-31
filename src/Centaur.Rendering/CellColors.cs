using Centaur.Core.Terminal;

namespace Centaur.Rendering;

/// <summary>
/// Resolves the colours a cell is actually drawn in, folding together the SGR attributes that
/// change colour (7 inverse, 2 faint) and the selection highlight. Shared by everything that
/// paints a cell - background runs, glyphs and decorations - so they cannot disagree.
/// </summary>
internal static class CellColors
{
    // SGR 7 and the selection highlight both swap foreground and background, so a selected
    // inverse cell inverts twice and renders as ordinary text - which is what users expect.
    static bool IsSwapped(Cell cell, int x, int y, TextSelection? selection) =>
        cell.inverse ^ (selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value));

    public static uint Foreground(Cell cell, int x, int y, TextSelection? selection)
    {
        var swapped = IsSwapped(cell, x, y, selection);
        var fg = swapped ? cell.background : cell.foreground;
        if (!cell.faint)
        {
            return fg;
        }

        // SGR 2 has no colour of its own: dim it halfway toward whatever it sits on.
        return Blend(fg, swapped ? cell.foreground : cell.background);
    }

    public static uint Background(Cell cell, int x, int y, TextSelection? selection) =>
        IsSwapped(cell, x, y, selection) ? cell.foreground : cell.background;

    /// <summary>Midpoint of two ARGB colours, keeping <paramref name="a"/>'s alpha.</summary>
    static uint Blend(uint a, uint b)
    {
        var alpha = a & 0xFF000000;
        var red = (((a >> 16) & 0xFF) + ((b >> 16) & 0xFF)) / 2;
        var green = (((a >> 8) & 0xFF) + ((b >> 8) & 0xFF)) / 2;
        var blue = ((a & 0xFF) + (b & 0xFF)) / 2;
        return alpha | (red << 16) | (green << 8) | blue;
    }
}
