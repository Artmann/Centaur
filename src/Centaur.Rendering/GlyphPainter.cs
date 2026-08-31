using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>What a <see cref="GlyphPainter.Collect"/> pass found in the grid: how many glyphs
/// to draw, plus the two things the rest of the frame needs to know about cells that carry no
/// glyph of their own.</summary>
internal readonly record struct GlyphCollection(int count, bool hasBlinking, bool hasDecorations);

/// <summary>
/// Turns cells into drawn glyphs: resolves a font per character - the fallback typeface for
/// codepoints the primary font lacks, in the synthetic bold/italic variant the cell asks for -
/// collects the visible glyphs of a grid into flat scratch arrays, then emits them batched by
/// (colour, font) so a full screen costs a handful of DrawText calls rather than one per cell.
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

    /// <summary>Gathers every drawable cell of the grid into the scratch arrays, reporting how
    /// many glyphs <see cref="DrawCollected"/> should emit.</summary>
    public GlyphCollection Collect(ScreenBuffer buffer, TextSelection? selection, bool blinkVisible)
    {
        buffers.Ensure(buffer.columns * buffer.rows);

        var count = 0;
        var blinking = false;
        var decorated = false;

        for (var y = 0; y < buffer.rows; y++)
        {
            var row = buffer.GetRow(y);
            var py = y * cellHeight + textYOffset;

            for (var x = 0; x < buffer.columns; x++)
            {
                var cell = row[x];
                blinking |= cell.blink;
                decorated |= IsDecorated(cell);

                if (!HasVisibleGlyph(cell, blinkVisible))
                {
                    continue;
                }

                var glyphFont = fallbacks.GetFont(
                    fallbacks.ResolveTypeface(cell.character),
                    cell.bold,
                    cell.italic
                );
                buffers.glyphs[count] = glyphFont.GetGlyph(cell.character);
                buffers.fonts[count] = glyphFont;
                buffers.positions[count] = new SKPoint(x * cellWidth, py);
                buffers.colors[count] = CellColors.Foreground(cell, x, y, selection);
                count++;
            }
        }

        return new GlyphCollection(count, blinking, decorated);
    }

    /// <summary>Whether the cell carries a stroke that is drawn even when its glyph is not.</summary>
    static bool IsDecorated(Cell cell) =>
        cell.underline != UnderlineStyle.None || cell.strikethrough || cell.overline;

    /// <summary>Blank cells carry no glyph, SGR 8 conceals it, and a blinking cell drops it for
    /// the off half of the blink cycle - but all three can still be decorated.</summary>
    static bool HasVisibleGlyph(Cell cell, bool blinkVisible) =>
        cell.character > ' ' && !cell.invisible && (blinkVisible || !cell.blink);

    /// <summary>Draws the collected glyphs, one text blob per (colour, font) run.</summary>
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
            var runFont = buffers.fonts[i];
            var runCount = 0;

            for (var j = i; j < count; j++)
            {
                if (
                    !buffers.drawn[j]
                    && buffers.colors[j] == color
                    && ReferenceEquals(buffers.fonts[j], runFont)
                )
                {
                    buffers.runGlyphs[runCount] = buffers.glyphs[j];
                    buffers.runPositions[runCount] = buffers.positions[j];
                    runCount++;
                    buffers.drawn[j] = true;
                }
            }

            DrawRun(canvas, runFont, runCount, color);
        }
    }

    /// <summary>Draws a single cell's glyph at a grid position - the inverted glyph under the
    /// cursor, which keeps the cell's own bold/italic styling.</summary>
    public void DrawGlyph(SKCanvas canvas, Cell cell, int column, int row, uint color)
    {
        var glyphFont = fallbacks.GetFont(
            fallbacks.ResolveTypeface(cell.character),
            cell.bold,
            cell.italic
        );
        buffers.runGlyphs[0] = glyphFont.GetGlyph(cell.character);
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

    public void Dispose()
    {
        fallbacks.Dispose();
        blobBuilder.Dispose();
        paint.Dispose();
    }
}
