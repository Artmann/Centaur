using System.Collections.Concurrent;
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
    SKTypeface?[] typefaceBuf = [];
    ushort[] runGlyphBuf = [];
    SKPoint[] runPosBuf = [];
    int bufferCapacity;

    // Font fallback: when the primary typeface lacks a glyph for a codepoint,
    // ask the system font manager for a typeface that has it (e.g. for box-drawing,
    // dingbats, color emoji). The system lookup (SKFontManager.MatchCharacter) is slow
    // and would freeze the UI thread, so it runs on a background resolver thread; the UI
    // thread only does the cheap primary-coverage check and reads the cache.
    //
    // codepoint -> matched fallback typeface (null = primary covers it, or no match found).
    // Read on the UI thread, written by the resolver, so it must be concurrent.
    internal readonly ConcurrentDictionary<char, SKTypeface?> fallbackTypefaceCache = new();

    // Codepoints already queued for background resolution, so each is enqueued at most once.
    readonly ConcurrentDictionary<char, byte> fallbackPending = new();
    readonly BlockingCollection<char> fallbackQueue = new();
    readonly Thread fallbackResolver;

    // Matched typefaces get their own SKFont sized identically to the primary font.
    // Only ever touched on the UI thread (GetFont), so a plain Dictionary is fine.
    readonly Dictionary<SKTypeface, SKFont> fallbackFontCache = new();

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

        fallbackResolver = new Thread(ResolveFallbacksLoop)
        {
            IsBackground = true,
            Name = "font-fallback-resolver",
        };
        fallbackResolver.Start();
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

                var tf = ResolveTypeface(cell.character);
                var glyphFont = GetFont(tf);
                glyphBuf[count] = glyphFont.GetGlyph(cell.character);
                typefaceBuf[count] = tf;
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
                var tf = ResolveTypeface(cursorCell.character);
                var cursorFont = GetFont(tf);
                var glyph = cursorFont.GetGlyph(cursorCell.character);
                var pos = new SKPoint(
                    buffer.cursorX * cellWidth,
                    buffer.cursorY * cellHeight + textYOffset
                );
                runGlyphBuf[0] = glyph;
                runPosBuf[0] = pos;
                using var blob = BuildBlob(cursorFont, runGlyphBuf, runPosBuf, 1);
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
            var tf = typefaceBuf[i];
            var runCount = 0;

            for (var j = i; j < count; j++)
            {
                if (!drawnBuf[j] && colorBuf[j] == color && typefaceBuf[j] == tf)
                {
                    runGlyphBuf[runCount] = glyphBuf[j];
                    runPosBuf[runCount] = posBuf[j];
                    runCount++;
                    drawnBuf[j] = true;
                }
            }

            var runFont = GetFont(tf);
            using var blob = BuildBlob(runFont, runGlyphBuf, runPosBuf, runCount);
            if (blob != null)
            {
                textPaint.Color = new SKColor(color);
                canvas.DrawText(blob, 0, 0, textPaint);
            }
        }
    }

    SKTextBlob? BuildBlob(SKFont blobFont, ushort[] glyphs, SKPoint[] positions, int count)
    {
        if (count == 0)
        {
            return null;
        }

        var run = blobBuilder.AllocatePositionedRun(blobFont, count);
        run.SetGlyphs(glyphs.AsSpan(0, count));
        run.SetPositions(positions.AsSpan(0, count));
        return blobBuilder.Build();
    }

    SKTypeface? ResolveTypeface(char c)
    {
        if (fallbackTypefaceCache.TryGetValue(c, out var cached))
        {
            return cached;
        }

        // Cheap, primary-font-only check — never touches SKFontManager.
        if (font.GetGlyph(c) != 0)
        {
            fallbackTypefaceCache[c] = null; // primary font covers it
            return null;
        }

        // Not covered: resolve in the background, draw with the primary font for now.
        // The continuous animation loop repaints once the resolver fills the cache.
        if (fallbackPending.TryAdd(c, 0))
        {
            fallbackQueue.Add(c);
        }
        return null;
    }

    void ResolveFallbacksLoop()
    {
        try
        {
            foreach (var c in fallbackQueue.GetConsumingEnumerable())
            {
                try
                {
                    // The first call here also forces SKFontManager.Default's one-time
                    // system font collection init off the UI thread.
                    fallbackTypefaceCache[c] = SKFontManager.Default.MatchCharacter(c);
                }
                catch
                {
                    // Give up gracefully on this codepoint; it draws with the primary
                    // font (tofu), which is the visible signal that no glyph was found.
                    fallbackTypefaceCache[c] = null;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Queue disposed during shutdown — expected.
        }
        catch (InvalidOperationException)
        {
            // GetConsumingEnumerable after CompleteAdding — expected on shutdown.
        }
    }

    SKFont GetFont(SKTypeface? tf)
    {
        if (tf == null)
        {
            return font;
        }

        if (fallbackFontCache.TryGetValue(tf, out var cached))
        {
            return cached;
        }

        var matched = new SKFont(tf, font.Size) { Subpixel = true };
        fallbackFontCache[tf] = matched;
        return matched;
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
            typefaceBuf = new SKTypeface?[cellCount];
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
        fallbackQueue.CompleteAdding();
        fallbackResolver.Join(TimeSpan.FromSeconds(1));
        fallbackQueue.Dispose();
        blobBuilder.Dispose();
        textPaint.Dispose();
        backgroundPaint.Dispose();
        cursorPaint.Dispose();
        readOnlyStrokePaint.Dispose();
        foreach (var f in fallbackFontCache.Values)
        {
            f.Dispose();
        }
        // The resolver thread has been joined, so the fallback cache is now stable.
        // Dispose the distinct system typefaces it resolved (skipping the primary
        // typeface, disposed below) to avoid leaking native handles in long sessions.
        var disposedFallbacks = new HashSet<SKTypeface>();
        foreach (var fallback in fallbackTypefaceCache.Values)
        {
            if (fallback != null && fallback != typeface && disposedFallbacks.Add(fallback))
            {
                fallback.Dispose();
            }
        }
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
