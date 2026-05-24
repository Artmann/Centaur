using System.Reflection;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public class TerminalRenderer : IDisposable
{
    internal readonly SKTypeface typeface;
    internal readonly SKFont font;
    readonly SKPaint textPaint;
    readonly SKPaint backgroundPaint;
    readonly SKPaint cursorPaint;
    readonly SKPaint readOnlyStrokePaint;
    readonly SKTextBlobBuilder blobBuilder = new();
    readonly TerminalTheme theme;
    readonly float textYOffset;

    // Pre-allocated buffers to avoid per-frame allocations
    ushort[] glyphBuf = [];
    SKPoint[] posBuf = [];
    uint[] colorBuf = [];
    bool[] drawnBuf = [];
    ushort[] runGlyphBuf = [];
    SKPoint[] runPosBuf = [];
    int bufferCapacity;

    public float cellWidth { get; }
    public float cellHeight { get; }

    public TerminalRenderer(TerminalTheme theme, float fontSize = 14f)
    {
        this.theme = theme;
        typeface = LoadEmbeddedFont() ?? SKTypeface.Default;
        font = new SKFont(typeface, fontSize);
        font.Subpixel = true;

        textPaint = new SKPaint { Color = new SKColor(theme.Foreground), IsAntialias = true };

        backgroundPaint = new SKPaint { Color = new SKColor(theme.Background) };

        cursorPaint = new SKPaint { Color = new SKColor(theme.Cursor) };

        readOnlyStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        cellWidth = font.MeasureText("M");
        cellHeight = fontSize * 1.2f;
        textYOffset = cellHeight - (cellHeight - font.Size) / 2;
    }

    public void Render(
        SKCanvas canvas,
        ScreenBuffer buffer,
        float canvasWidth,
        TextSelection? selection = null,
        IReadOnlyList<IRenderOverlay>? overlays = null,
        bool cursorVisible = true,
        bool readOnly = false
    )
    {
        canvas.Clear(new SKColor(theme.Background));

        var cellCount = buffer.columns * buffer.rows;
        EnsureBuffers(cellCount);

        // Pass 1: Draw background color runs
        for (var y = 0; y < buffer.rows; y++)
        {
            var row = buffer.GetRow(y);
            var py = y * cellHeight;

            var bgRunStart = 0;
            var bgRunColor = GetBgColor(row[0], 0, y, selection);

            for (var x = 1; x <= buffer.columns; x++)
            {
                var bg = x < buffer.columns ? GetBgColor(row[x], x, y, selection) : uint.MaxValue;

                if (bg != bgRunColor)
                {
                    if (bgRunColor != theme.Background)
                    {
                        backgroundPaint.Color = new SKColor(bgRunColor);
                        canvas.DrawRect(
                            bgRunStart * cellWidth,
                            py,
                            (x - bgRunStart) * cellWidth,
                            cellHeight,
                            backgroundPaint
                        );
                    }
                    bgRunStart = x;
                    bgRunColor = bg;
                }
            }
        }

        // Pass 2: Collect all visible glyphs with positions and colors
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

                glyphBuf[count] = font.GetGlyph(cell.character);
                posBuf[count] = new SKPoint(x * cellWidth, py);
                colorBuf[count] = GetFgColor(cell, x, y, selection);
                count++;
            }
        }

        // Pass 3: Draw glyphs batched by color using SKTextBlob
        if (count > 0)
        {
            DrawGlyphsByColor(canvas, count);
        }

        // Draw cursor
        if (
            cursorVisible
            && buffer.cursorX >= 0
            && buffer.cursorX < buffer.columns
            && buffer.cursorY >= 0
            && buffer.cursorY < buffer.rows
        )
        {
            cursorPaint.Color = new SKColor(theme.Cursor);
            canvas.DrawRect(
                buffer.cursorX * cellWidth,
                buffer.cursorY * cellHeight,
                cellWidth,
                cellHeight,
                cursorPaint
            );

            // Re-draw character under cursor with inverted color so it's visible
            var cursorCell = buffer[buffer.cursorX, buffer.cursorY];
            if (cursorCell.character > ' ')
            {
                var glyph = font.GetGlyph(cursorCell.character);
                var pos = new SKPoint(
                    buffer.cursorX * cellWidth,
                    buffer.cursorY * cellHeight + textYOffset
                );
                runGlyphBuf[0] = glyph;
                runPosBuf[0] = pos;
                using var blob = BuildBlob(runGlyphBuf, runPosBuf, 1);
                if (blob != null)
                {
                    textPaint.Color = new SKColor(theme.Background);
                    canvas.DrawText(blob, 0, 0, textPaint);
                }
            }
        }

        if (readOnly)
        {
            DrawReadOnlyBadge(canvas, canvasWidth);
        }

        if (overlays != null)
        {
            foreach (var overlay in overlays)
            {
                overlay.Render(canvas, canvasWidth, theme, font, typeface);
            }
        }
    }

    void DrawReadOnlyBadge(SKCanvas canvas, float canvasWidth)
    {
        const string text = "READ-ONLY";
        var padding = 6f;
        var textWidth = font.MeasureText(text);
        var height = font.Size * 1.4f;
        var width = textWidth + padding * 2;
        var x = canvasWidth - width - padding;
        var y = padding;

        var color = new SKColor(theme.Palette[1]);

        readOnlyStrokePaint.Color = color;
        canvas.DrawRoundRect(x, y, width, height, 3f, 3f, readOnlyStrokePaint);

        textPaint.Color = color;
        canvas.DrawText(text, x + padding, y + font.Size, font, textPaint);
        textPaint.Color = new SKColor(theme.Foreground);
    }

    void DrawGlyphsByColor(SKCanvas canvas, int count)
    {
        Array.Clear(drawnBuf, 0, count);

        for (var i = 0; i < count; i++)
        {
            if (drawnBuf[i])
            {
                continue;
            }

            var color = colorBuf[i];
            var runCount = 0;

            for (var j = i; j < count; j++)
            {
                if (colorBuf[j] == color)
                {
                    runGlyphBuf[runCount] = glyphBuf[j];
                    runPosBuf[runCount] = posBuf[j];
                    runCount++;
                    drawnBuf[j] = true;
                }
            }

            using var blob = BuildBlob(runGlyphBuf, runPosBuf, runCount);
            if (blob != null)
            {
                textPaint.Color = new SKColor(color);
                canvas.DrawText(blob, 0, 0, textPaint);
            }
        }
    }

    SKTextBlob? BuildBlob(ushort[] glyphs, SKPoint[] positions, int count)
    {
        if (count == 0)
        {
            return null;
        }

        var run = blobBuilder.AllocatePositionedRun(font, count);
        run.SetGlyphs(glyphs.AsSpan(0, count));
        run.SetPositions(positions.AsSpan(0, count));
        return blobBuilder.Build();
    }

    void EnsureBuffers(int cellCount)
    {
        if (bufferCapacity < cellCount)
        {
            bufferCapacity = cellCount;
            glyphBuf = new ushort[cellCount];
            posBuf = new SKPoint[cellCount];
            colorBuf = new uint[cellCount];
            drawnBuf = new bool[cellCount];
            runGlyphBuf = new ushort[cellCount];
            runPosBuf = new SKPoint[cellCount];
        }
    }

    static uint GetFgColor(Cell cell, int x, int y, TextSelection? selection)
    {
        var selected = selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value);
        return selected ? cell.background : cell.foreground;
    }

    static uint GetBgColor(Cell cell, int x, int y, TextSelection? selection)
    {
        var selected = selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value);
        return selected ? cell.foreground : cell.background;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        blobBuilder.Dispose();
        textPaint.Dispose();
        backgroundPaint.Dispose();
        cursorPaint.Dispose();
        readOnlyStrokePaint.Dispose();
        font.Dispose();
        typeface.Dispose();
    }

    static SKTypeface? LoadEmbeddedFont()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Centaur.Rendering.Fonts.JetBrainsMono-Regular.ttf";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        return stream != null ? SKTypeface.FromStream(stream) : null;
    }
}
