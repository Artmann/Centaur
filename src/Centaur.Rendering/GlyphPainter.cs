using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Turns cells into drawn glyphs: resolves a typeface per character, collects the visible
/// glyphs of a grid into flat scratch arrays, then emits them batched by (colour, typeface)
/// so a full screen costs a handful of DrawText calls rather than one per cell.
/// </summary>
internal sealed class GlyphPainter : IDisposable
{
    readonly SKFont font;
    readonly FontFallbackResolver fallbacks;
    readonly GlyphRunBuffers buffers = new();
    readonly SKTextBlobBuilder blobBuilder = new();
    readonly SKPaint paint = new() { IsAntialias = true };
    readonly float cellWidth;
    readonly float cellHeight;
    readonly float textYOffset;

    public GlyphPainter(SKFont font, float cellWidth, float cellHeight, float textYOffset)
    {
        this.font = font;
        this.cellWidth = cellWidth;
        this.cellHeight = cellHeight;
        this.textYOffset = textYOffset;
        fallbacks = new FontFallbackResolver(font);
    }

    /// <summary>Test hook: has the background fallback resolver answered for this codepoint?</summary>
    internal bool IsFallbackResolved(char c) => fallbacks.IsResolved(c);

    /// <summary>Gathers every non-blank cell of the grid into the scratch arrays, returning how
    /// many glyphs <see cref="DrawCollected"/> should emit.</summary>
    public int Collect(ScreenBuffer buffer, TextSelection? selection)
    {
        buffers.Ensure(buffer.columns * buffer.rows);

        var count = 0;
        for (var y = 0; y < buffer.rows; y++)
        {
            var row = buffer.GetRow(y);
            var py = y * cellHeight + textYOffset;

            for (var x = 0; x < buffer.columns; x++)
            {
                var cell = row[x];
                if (cell.character <= ' ')
                {
                    continue;
                }

                var tf = fallbacks.ResolveTypeface(cell.character);
                buffers.glyphs[count] = fallbacks.GetFont(tf).GetGlyph(cell.character);
                buffers.typefaces[count] = tf;
                buffers.positions[count] = new SKPoint(x * cellWidth, py);
                buffers.colors[count] = ForegroundOf(cell, x, y, selection);
                count++;
            }
        }
        return count;
    }

    /// <summary>Draws the collected glyphs, one text blob per (colour, typeface) run.</summary>
    public void DrawCollected(SKCanvas canvas, int count)
    {
        Array.Clear(buffers.drawn, 0, count);

        for (var i = 0; i < count; i++)
        {
            if (buffers.drawn[i])
            {
                continue;
            }

            var color = buffers.colors[i];
            var tf = buffers.typefaces[i];
            var runCount = 0;

            for (var j = i; j < count; j++)
            {
                if (!buffers.drawn[j] && buffers.colors[j] == color && buffers.typefaces[j] == tf)
                {
                    buffers.runGlyphs[runCount] = buffers.glyphs[j];
                    buffers.runPositions[runCount] = buffers.positions[j];
                    runCount++;
                    buffers.drawn[j] = true;
                }
            }

            DrawRun(canvas, fallbacks.GetFont(tf), runCount, color);
        }
    }

    /// <summary>Draws a single character at a grid position - the inverted glyph under the cursor.</summary>
    public void DrawGlyph(SKCanvas canvas, char c, int column, int row, uint color)
    {
        var tf = fallbacks.ResolveTypeface(c);
        var glyphFont = fallbacks.GetFont(tf);
        buffers.runGlyphs[0] = glyphFont.GetGlyph(c);
        buffers.runPositions[0] = new SKPoint(column * cellWidth, row * cellHeight + textYOffset);
        DrawRun(canvas, glyphFont, 1, color);
    }

    /// <summary>Draws a plain string in the primary font - overlay chrome, not grid content.</summary>
    public void DrawText(SKCanvas canvas, string text, float x, float y, uint color)
    {
        paint.Color = new SKColor(color);
        canvas.DrawText(text, x, y, font, paint);
    }

    void DrawRun(SKCanvas canvas, SKFont runFont, int count, uint color)
    {
        if (count == 0)
        {
            return;
        }

        var run = blobBuilder.AllocatePositionedRun(runFont, count);
        run.SetGlyphs(buffers.runGlyphs.AsSpan(0, count));
        run.SetPositions(buffers.runPositions.AsSpan(0, count));

        using var blob = blobBuilder.Build();
        if (blob != null)
        {
            paint.Color = new SKColor(color);
            canvas.DrawText(blob, 0, 0, paint);
        }
    }

    static uint ForegroundOf(Cell cell, int x, int y, TextSelection? selection)
    {
        var selected = selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value);
        return selected ? cell.background : cell.foreground;
    }

    public void Dispose()
    {
        fallbacks.Dispose();
        blobBuilder.Dispose();
        paint.Dispose();
    }
}
