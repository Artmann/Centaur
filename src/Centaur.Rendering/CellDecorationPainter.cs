using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Draws the strokes that sit around a glyph rather than replacing it: underlines (SGR 4 and
/// its 4:x variants), strikethrough (SGR 9) and overline (SGR 53). Contiguous cells sharing a
/// colour and style are batched into one stroke, so a fully underlined line costs a single draw.
/// </summary>
internal sealed class CellDecorationPainter : IDisposable
{
    enum Decoration
    {
        Underline,
        Strikeout,
        Overline,
    }

    /// <summary>The parts of a frame every row and cell is measured against.</summary>
    readonly record struct Frame(TextSelection? selection, bool blinkVisible);

    /// <summary>What one cell contributes to the decoration currently being scanned for.</summary>
    readonly record struct Span(bool active, uint color, UnderlineStyle style);

    /// <summary>One batched stroke: a horizontal span of cells sharing a colour and style.</summary>
    readonly record struct Run(
        float x0,
        float x1,
        float top,
        uint color,
        Decoration kind,
        UnderlineStyle style
    );

    readonly SKPaint paint = new() { IsAntialias = true };

    // Reused across frames: SKPath.Reset() keeps the allocated segment capacity, so drawing
    // curly underlines stays inside the renderer's zero-allocation-per-frame budget.
    readonly SKPath curlyPath = new();

    readonly float cellWidth;
    readonly float cellHeight;

    // Vertical geometry for underline/strikethrough/overline strokes, relative to the top of a
    // cell, plus their common thickness. Computed once from the font metrics.
    readonly float thickness;
    readonly float underlineOffset;
    readonly float strikethroughOffset;
    readonly float overlineOffset;

    public CellDecorationPainter(SKFont font, float cellWidth, float cellHeight, float textYOffset)
    {
        this.cellWidth = cellWidth;
        this.cellHeight = cellHeight;

        // Decoration geometry is snapped to whole pixels: a one-pixel line at a fractional y
        // would otherwise smear into two rows of half-coverage and read as a grey smudge.
        var fontSize = font.Size;
        var metrics = font.Metrics;
        thickness = MathF.Max(1f, MathF.Round(fontSize / 14f));
        var belowBaseline = MathF.Max(1f, metrics.UnderlinePosition ?? fontSize * 0.12f);
        underlineOffset = MathF.Floor(
            MathF.Min(textYOffset + belowBaseline, cellHeight - thickness)
        );
        strikethroughOffset = MathF.Floor(textYOffset - fontSize * 0.3f);
        overlineOffset = MathF.Floor(MathF.Max(0f, textYOffset + metrics.Ascent));
    }

    /// <summary>Draws every decoration in the grid. Called after the glyphs, so a strikethrough
    /// lands on top of the character it crosses out.</summary>
    public void Draw(
        SKCanvas canvas,
        ScreenBuffer buffer,
        TextSelection? selection,
        bool blinkVisible
    )
    {
        var frame = new Frame(selection, blinkVisible);
        for (var y = 0; y < buffer.rows; y++)
        {
            var row = buffer.GetRow(y);
            DrawRow(canvas, row, y, frame, Decoration.Underline);
            DrawRow(canvas, row, y, frame, Decoration.Strikeout);
            DrawRow(canvas, row, y, frame, Decoration.Overline);
        }
    }

    /// <summary>
    /// Draws one decoration kind for a single row, batching contiguous cells that share a colour
    /// (and, for underlines, a style) into one stroke.
    /// </summary>
    void DrawRow(SKCanvas canvas, ReadOnlySpan<Cell> row, int y, Frame frame, Decoration kind)
    {
        var top = y * cellHeight;
        var runStart = -1;
        var open = default(Span);

        // One past the end closes any run still open at the right edge of the row.
        for (var x = 0; x <= row.Length; x++)
        {
            var span = x < row.Length ? SpanAt(row[x], x, y, frame, kind) : default;

            if (span.active && runStart >= 0 && span == open)
            {
                continue;
            }

            if (runStart >= 0)
            {
                DrawOne(
                    canvas,
                    new Run(runStart * cellWidth, x * cellWidth, top, open.color, kind, open.style)
                );
                runStart = -1;
            }

            if (span.active)
            {
                runStart = x;
                open = span;
            }
        }
    }

    /// <summary>What a single cell asks for of one decoration kind - inactive for a cell that
    /// lacks it, or is blinking through the off half of its cycle.</summary>
    static Span SpanAt(Cell cell, int x, int y, Frame frame, Decoration kind)
    {
        if (!frame.blinkVisible && cell.blink)
        {
            return default;
        }

        var foreground = CellColors.Foreground(cell, x, y, frame.selection);

        if (kind == Decoration.Strikeout)
        {
            return new Span(cell.strikethrough, foreground, UnderlineStyle.None);
        }

        if (kind == Decoration.Overline)
        {
            return new Span(cell.overline, foreground, UnderlineStyle.None);
        }

        // 0 is the "no explicit SGR 58 colour" sentinel: inherit the text colour.
        var color = cell.underlineColor != 0 ? cell.underlineColor : foreground;
        return new Span(cell.underline != UnderlineStyle.None, color, cell.underline);
    }

    void DrawOne(SKCanvas canvas, Run run)
    {
        paint.Color = new SKColor(run.color);
        paint.Style = SKPaintStyle.Fill;

        var width = run.x1 - run.x0;

        if (run.kind == Decoration.Strikeout)
        {
            canvas.DrawRect(run.x0, run.top + strikethroughOffset, width, thickness, paint);
            return;
        }

        if (run.kind == Decoration.Overline)
        {
            canvas.DrawRect(run.x0, run.top + overlineOffset, width, thickness, paint);
            return;
        }

        var y = run.top + underlineOffset;
        switch (run.style)
        {
            case UnderlineStyle.Double:
                canvas.DrawRect(run.x0, y, width, thickness, paint);
                canvas.DrawRect(run.x0, y - thickness * 2, width, thickness, paint);
                break;
            case UnderlineStyle.Curly:
                DrawCurly(canvas, run.x0, run.x1, y);
                break;
            case UnderlineStyle.Dotted:
            case UnderlineStyle.Dashed:
                DrawDashed(canvas, run.x0, run.x1, y, run.style);
                break;
            default:
                canvas.DrawRect(run.x0, y, width, thickness, paint);
                break;
        }
    }

    /// <summary>Dots and dashes are drawn as pixel-aligned rects rather than via an
    /// SKPathEffect: no per-frame effect allocation, and the segments stay crisp.</summary>
    void DrawDashed(SKCanvas canvas, float x0, float x1, float y, UnderlineStyle style)
    {
        var on = style == UnderlineStyle.Dashed ? thickness * 3 : thickness;
        var off = style == UnderlineStyle.Dashed ? thickness * 2 : thickness;

        for (var x = x0; x < x1; x += on + off)
        {
            canvas.DrawRect(x, y, MathF.Min(on, x1 - x), thickness, paint);
        }
    }

    void DrawCurly(SKCanvas canvas, float x0, float x1, float baseY)
    {
        // A quadratic reaches half of its control-point offset, so a control offset of 2a gives
        // a wave of amplitude a - keeping the whole squiggle inside the cell.
        var amplitude = thickness;
        var centerY = baseY - amplitude;
        var period = MathF.Max(4f, cellWidth / 2f);

        curlyPath.Reset();
        curlyPath.MoveTo(x0, centerY);

        var up = true;
        for (var x = x0; x < x1; x += period)
        {
            var next = MathF.Min(x + period, x1);
            var control = centerY + (up ? -amplitude * 2 : amplitude * 2);
            curlyPath.QuadTo((x + next) / 2f, control, next, centerY);
            up = !up;
        }

        paint.Style = SKPaintStyle.Stroke;
        paint.StrokeWidth = thickness;
        canvas.DrawPath(curlyPath, paint);
        paint.Style = SKPaintStyle.Fill;
    }

    public void Dispose()
    {
        paint.Dispose();
        curlyPath.Dispose();
    }
}
