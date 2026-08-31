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
    readonly SKPaint bellFlashPaint;
    readonly TerminalTheme theme;
    readonly TerminalAppearance appearance;
    readonly SKColor clearColor;
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

    /// <summary>True when the cursor itself blinks, which needs the same wall-clock heartbeat
    /// that SGR 5/6 cells do even on a terminal producing no output.</summary>
    public bool CursorBlinks => appearance.CursorBlink;

    public TerminalRenderer(
        TerminalTheme theme,
        TerminalAppearance? appearance = null,
        RenderProfiler? profiler = null
    )
    {
        this.theme = theme;
        this.appearance = appearance ?? TerminalAppearance.Default;
        this.profiler = profiler;

        var fontSize = this.appearance.FontSize;
        typeface = LoadEmbeddedFont() ?? SKTypeface.Default;
        font = new SKFont(typeface, fontSize);
        font.Subpixel = true;

        clearColor = new SKColor(theme.Background).WithAlpha(
            (byte)Math.Clamp(this.appearance.BackgroundOpacity * 255f, 0f, 255f)
        );

        backgroundPaint = new SKPaint { Color = new SKColor(theme.Background) };

        cursorPaint = new SKPaint { Color = new SKColor(theme.Cursor) };

        readOnlyStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        // A wash of the text colour over the whole grid: visible on any theme, and gone again
        // before the next blink half-cycle, so it reads as a flash rather than a repaint.
        bellFlashPaint = new SKPaint { Color = new SKColor(theme.Foreground).WithAlpha(56) };

        cellWidth = font.MeasureText("M");
        cellHeight = fontSize * this.appearance.LineHeight;
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
        bool blinkVisible = true,
        bool bellFlash = false
    )
    {
        var clock = FrameClock.Start(profiler?.Enabled == true);

        canvas.Clear(clearColor);
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

        // A steady cursor ignores the blink phase entirely; only a blinking one goes dark on it.
        var cursor = cursorVisible && (blinkVisible || !appearance.CursorBlink);
        DrawChrome(canvas, buffer, canvasWidth, new PaneMarks(cursor, readOnly, bellFlash));
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

    /// <summary>Which of the pane's own marks a frame draws over the text.</summary>
    readonly record struct PaneMarks(bool Cursor, bool ReadOnly, bool BellFlash);

    /// <summary>The marks that belong to the pane rather than to its text: the cursor, the
    /// read-only badge, and the visual bell.</summary>
    void DrawChrome(SKCanvas canvas, ScreenBuffer buffer, float canvasWidth, PaneMarks marks)
    {
        if (marks.Cursor)
        {
            DrawCursor(canvas, buffer);
        }

        if (marks.ReadOnly)
        {
            DrawReadOnlyBadge(canvas, canvasWidth);
        }

        // Last, and over everything, because the visual bell is a wash across the whole pane.
        if (marks.BellFlash)
        {
            canvas.DrawRect(0, 0, canvasWidth, buffer.rows * cellHeight, bellFlashPaint);
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

        var x = column * cellWidth;
        var y = row * cellHeight;

        // A block covers the cell and the character is redrawn on top of it inverted. The thin
        // styles sit beside the character instead, so it stays in its own colour.
        switch (appearance.CursorStyle)
        {
            case CursorStyle.Underline:
                var thickness = Math.Max(1f, cellHeight * 0.1f);
                canvas.DrawRect(x, y + cellHeight - thickness, cellWidth, thickness, cursorPaint);
                return;
            case CursorStyle.Bar:
                canvas.DrawRect(x, y, Math.Max(1f, cellWidth * 0.15f), cellHeight, cursorPaint);
                return;
        }

        canvas.DrawRect(x, y, cellWidth, cellHeight, cursorPaint);

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
        bellFlashPaint.Dispose();
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
