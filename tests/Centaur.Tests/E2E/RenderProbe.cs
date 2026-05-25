using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;

namespace Centaur.Tests;

/// <summary>
/// Renders a <see cref="ScreenBuffer"/> to an offscreen bitmap with the real
/// <see cref="TerminalRenderer"/> (the same render path the on-screen draw operation uses,
/// see TerminalControl.TerminalDrawOperation) and samples pixels back out. This is how the
/// e2e tests assert that output "renders correctly" without committing brittle golden images.
/// </summary>
static class RenderProbe
{
    /// <summary>Render the buffer to a CPU bitmap sized to the grid. Caller owns the result.</summary>
    public static SKBitmap RenderToBitmap(
        ScreenBuffer buffer,
        TerminalRenderer renderer,
        bool cursorVisible = true
    )
    {
        var width = (int)Math.Ceiling(buffer.columns * renderer.cellWidth);
        var height = (int)Math.Ceiling(buffer.rows * renderer.cellHeight);

        var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888));
        using var canvas = new SKCanvas(bitmap);
        renderer.Render(canvas, buffer, width, cursorVisible: cursorVisible);
        return bitmap;
    }

    /// <summary>Color at the center of a cell.</summary>
    public static SKColor CellCenterPixel(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row
    )
    {
        var px = Clamp((int)((col + 0.5f) * renderer.cellWidth), bitmap.Width);
        var py = Clamp((int)((row + 0.5f) * renderer.cellHeight), bitmap.Height);
        return bitmap.GetPixel(px, py);
    }

    /// <summary>Count pixels in a cell that differ from <paramref name="background"/> — i.e. ink
    /// (a glyph, cursor block, or colored background) was actually drawn in that cell.</summary>
    public static int ForegroundPixelCount(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row,
        SKColor background,
        int tolerance = 16
    )
    {
        var count = 0;
        ForEachCellPixel(
            bitmap,
            renderer,
            col,
            row,
            p =>
            {
                if (!ColorsClose(p, background, tolerance))
                {
                    count++;
                }
            }
        );
        return count;
    }

    /// <summary>Average color of the non-background pixels in a cell — the rendered ink color,
    /// robust to antialiasing because it ignores the background and averages the rest.</summary>
    public static SKColor DominantForegroundColor(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row,
        SKColor background,
        int tolerance = 16
    )
    {
        long r = 0,
            g = 0,
            b = 0,
            n = 0;
        ForEachCellPixel(
            bitmap,
            renderer,
            col,
            row,
            p =>
            {
                if (!ColorsClose(p, background, tolerance))
                {
                    r += p.Red;
                    g += p.Green;
                    b += p.Blue;
                    n++;
                }
            }
        );
        if (n == 0)
        {
            return background;
        }
        return new SKColor((byte)(r / n), (byte)(g / n), (byte)(b / n));
    }

    /// <summary>Count pixels in a cell whose color is within tolerance of <paramref name="target"/>.</summary>
    public static int CountPixelsClose(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row,
        SKColor target,
        int tolerance = 24
    )
    {
        var count = 0;
        ForEachCellPixel(
            bitmap,
            renderer,
            col,
            row,
            p =>
            {
                if (ColorsClose(p, target, tolerance))
                {
                    count++;
                }
            }
        );
        return count;
    }

    /// <summary>Per-channel tolerance compare (ignores alpha).</summary>
    public static bool ColorsClose(SKColor a, SKColor b, int tolerance = 24) =>
        Math.Abs(a.Red - b.Red) <= tolerance
        && Math.Abs(a.Green - b.Green) <= tolerance
        && Math.Abs(a.Blue - b.Blue) <= tolerance;

    /// <summary>Convenience: a theme color (0xAARRGGBB) as an <see cref="SKColor"/>, matching
    /// how <see cref="TerminalRenderer"/> converts theme colors.</summary>
    public static SKColor ToColor(uint argb) => new(argb);

    static void ForEachCellPixel(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row,
        Action<SKColor> visit
    )
    {
        var x0 = (int)(col * renderer.cellWidth);
        var y0 = (int)(row * renderer.cellHeight);
        var x1 = Math.Min((int)Math.Ceiling((col + 1) * renderer.cellWidth), bitmap.Width);
        var y1 = Math.Min((int)Math.Ceiling((row + 1) * renderer.cellHeight), bitmap.Height);

        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                visit(bitmap.GetPixel(x, y));
            }
        }
    }

    static int Clamp(int value, int size) => Math.Clamp(value, 0, size - 1);
}
