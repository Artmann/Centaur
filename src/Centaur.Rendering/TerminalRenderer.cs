using System.Reflection;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public class TerminalRenderer : IDisposable
{
    internal readonly SKTypeface typeface;
    internal readonly SKFont font;
    readonly SKPaint backgroundPaint;
    readonly SKPaint cursorPaint;
    readonly SKPaint readOnlyStrokePaint;
    readonly TerminalTheme theme;
    readonly RenderProfiler? profiler;
    readonly GlyphPainter glyphs;

    public float cellWidth { get; }
    public float cellHeight { get; }

    public TerminalRenderer(
        TerminalTheme theme,
        float fontSize = 14f,
        RenderProfiler? profiler = null
    )
    {
        this.theme = theme;
        this.profiler = profiler;
        typeface = LoadEmbeddedFont() ?? SKTypeface.Default;
        font = new SKFont(typeface, fontSize);
        font.Subpixel = true;

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
        var textYOffset = cellHeight - (cellHeight - font.Size) / 2;

        glyphs = new GlyphPainter(font, cellWidth, cellHeight, textYOffset);
    }

    /// <summary>Test hook: has font fallback for this codepoint been resolved off-thread yet?</summary>
    internal bool IsFallbackResolved(char c) => glyphs.IsFallbackResolved(c);

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
        var clock = FrameClock.Start(profiler?.Enabled == true);

        canvas.Clear(new SKColor(theme.Background));
        clock.Mark();

        DrawBackgroundRuns(canvas, buffer, selection);
        clock.Mark();

        var count = glyphs.Collect(buffer, selection);
        clock.Mark();

        glyphs.DrawCollected(canvas, count);
        clock.Mark();

        if (cursorVisible)
        {
            DrawCursor(canvas, buffer);
        }
        if (readOnly)
        {
            DrawReadOnlyBadge(canvas, canvasWidth);
        }
        clock.Mark();

        DrawOverlays(canvas, canvasWidth, overlays);
        clock.Mark();

        if (clock.TryFinish(out var timings))
        {
            profiler!.RecordFrame(timings);
        }
    }

    /// <summary>Fills each row's background as horizontal runs of equal colour, skipping the
    /// runs that match the theme background the canvas was already cleared to.</summary>
    void DrawBackgroundRuns(SKCanvas canvas, ScreenBuffer buffer, TextSelection? selection)
    {
        for (var y = 0; y < buffer.rows; y++)
        {
            var row = buffer.GetRow(y);
            var py = y * cellHeight;

            var runStart = 0;
            var runColor = BackgroundOf(row[0], 0, y, selection);

            for (var x = 1; x <= buffer.columns; x++)
            {
                // uint.MaxValue past the last column forces the final run to flush.
                var bg = x < buffer.columns ? BackgroundOf(row[x], x, y, selection) : uint.MaxValue;
                if (bg == runColor)
                {
                    continue;
                }

                if (runColor != theme.Background)
                {
                    backgroundPaint.Color = new SKColor(runColor);
                    var width = (x - runStart) * cellWidth;
                    canvas.DrawRect(runStart * cellWidth, py, width, cellHeight, backgroundPaint);
                }
                runStart = x;
                runColor = bg;
            }
        }
    }

    void DrawCursor(SKCanvas canvas, ScreenBuffer buffer)
    {
        var column = buffer.cursorX;
        var row = buffer.cursorY;
        if (column < 0 || column >= buffer.columns || row < 0 || row >= buffer.rows)
        {
            return;
        }

        cursorPaint.Color = new SKColor(theme.Cursor);
        canvas.DrawRect(column * cellWidth, row * cellHeight, cellWidth, cellHeight, cursorPaint);

        // Re-draw the character under the cursor inverted, so it stays readable.
        var cell = buffer[column, row];
        if (cell.character > ' ')
        {
            glyphs.DrawGlyph(canvas, cell.character, column, row, theme.Background);
        }
    }

    void DrawReadOnlyBadge(SKCanvas canvas, float canvasWidth)
    {
        const string text = "READ-ONLY";
        var padding = 6f;
        var height = font.Size * 1.4f;
        var width = font.MeasureText(text) + padding * 2;
        var x = canvasWidth - width - padding;
        var y = padding;

        readOnlyStrokePaint.Color = new SKColor(theme.Palette[1]);
        canvas.DrawRoundRect(x, y, width, height, 3f, 3f, readOnlyStrokePaint);

        glyphs.DrawText(canvas, text, x + padding, y + font.Size, theme.Palette[1]);
    }

    void DrawOverlays(SKCanvas canvas, float canvasWidth, IReadOnlyList<IRenderOverlay>? overlays)
    {
        if (overlays == null)
        {
            return;
        }

        foreach (var overlay in overlays)
        {
            overlay.Render(canvas, canvasWidth, theme, font, typeface);
        }
    }

    static uint BackgroundOf(Cell cell, int x, int y, TextSelection? selection)
    {
        var selected = selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value);
        return selected ? cell.foreground : cell.background;
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        glyphs.Dispose();
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
