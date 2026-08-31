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
    readonly CellDecorationPainter decorations;

    public float cellWidth { get; }
    public float cellHeight { get; }

    /// <summary>
    /// True when the last rendered frame contained at least one cell with SGR 5/6 (blink) set.
    /// The control uses this to keep the frame scheduler's heartbeat running so the blink phase
    /// keeps advancing on an otherwise idle terminal.
    /// </summary>
    public bool HasBlinkingCells { get; private set; }

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
        decorations = new CellDecorationPainter(font, cellWidth, cellHeight, textYOffset);
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
        bool readOnly = false,
        bool blinkVisible = true
    )
    {
        var clock = FrameClock.Start(profiler?.Enabled == true);

        canvas.Clear(new SKColor(theme.Background));
        clock.Mark();

        DrawBackgroundRuns(canvas, buffer, selection);
        clock.Mark();

        var collected = glyphs.Collect(buffer, selection, blinkVisible);
        HasBlinkingCells = collected.hasBlinking;
        clock.Mark();

        glyphs.DrawCollected(canvas, collected.count);

        // Drawn after the glyphs, so a strikethrough lands on top of the character it crosses out.
        if (collected.hasDecorations)
        {
            decorations.Draw(canvas, buffer, selection, blinkVisible);
        }
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
            var runColor = CellColors.Background(row[0], 0, y, selection);

            for (var x = 1; x <= buffer.columns; x++)
            {
                // uint.MaxValue past the last column forces the final run to flush.
                var bg =
                    x < buffer.columns
                        ? CellColors.Background(row[x], x, y, selection)
                        : uint.MaxValue;
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
        if (cell.character > ' ' && !cell.invisible)
        {
            glyphs.DrawGlyph(canvas, cell, column, row, theme.Background);
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

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        glyphs.Dispose();
        decorations.Dispose();
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
