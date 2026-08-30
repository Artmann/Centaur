using System.Collections.Concurrent;
using System.Diagnostics;
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
    readonly SKPaint decorationPaint;
    readonly SKTextBlobBuilder blobBuilder = new();

    // Reused across frames: SKPath.Reset() keeps the allocated segment capacity, so drawing
    // curly underlines stays inside the renderer's zero-allocation-per-frame budget.
    readonly SKPath curlyPath = new();

    readonly TerminalTheme theme;
    readonly float textYOffset;
    readonly RenderProfiler? profiler;

    // Vertical geometry for underline/strikethrough/overline strokes, relative to the top of a
    // cell, plus their common thickness. Computed once from the font metrics.
    readonly float decorationThickness;
    readonly float underlineOffset;
    readonly float strikethroughOffset;
    readonly float overlineOffset;

    // Pre-allocated buffers to avoid per-frame allocations
    ushort[] glyphBuf = [];
    SKPoint[] posBuf = [];
    uint[] colorBuf = [];
    bool[] drawnBuf = [];
    SKFont[] fontBuf = [];
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

    // One SKFont per (typeface, bold, italic) combination, sized identically to the primary
    // font. Only JetBrains Mono Regular is embedded, so bold and italic are synthesised via
    // Embolden/SkewX — that keeps the typeface (and therefore every glyph id) unchanged across
    // variants, which is what lets a single glyph buffer serve all of them.
    // Only ever touched on the UI thread (GetFont), so a plain Dictionary is fine.
    readonly Dictionary<(SKTypeface? typeface, bool bold, bool italic), SKFont> styledFontCache =
        new();

    // Horizontal shear applied for synthetic italics: negative leans the top of the glyph right.
    const float italicSkew = -0.22f;

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

        textPaint = new SKPaint { Color = new SKColor(theme.Foreground), IsAntialias = true };

        backgroundPaint = new SKPaint { Color = new SKColor(theme.Background) };

        cursorPaint = new SKPaint { Color = new SKColor(theme.Cursor) };

        readOnlyStrokePaint = new SKPaint
        {
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true,
        };

        decorationPaint = new SKPaint { IsAntialias = true };

        cellWidth = font.MeasureText("M");
        cellHeight = fontSize * 1.2f;
        textYOffset = cellHeight - (cellHeight - font.Size) / 2;

        // Decoration geometry is snapped to whole pixels: a one-pixel line at a fractional y
        // would otherwise smear into two rows of half-coverage and read as a grey smudge.
        var metrics = font.Metrics;
        decorationThickness = MathF.Max(1f, MathF.Round(fontSize / 14f));
        var belowBaseline = MathF.Max(1f, metrics.UnderlinePosition ?? fontSize * 0.12f);
        underlineOffset = MathF.Floor(
            MathF.Min(textYOffset + belowBaseline, cellHeight - decorationThickness)
        );
        strikethroughOffset = MathF.Floor(textYOffset - fontSize * 0.3f);
        overlineOffset = MathF.Floor(MathF.Max(0f, textYOffset + metrics.Ascent));

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
        bool readOnly = false,
        bool blinkVisible = true
    )
    {
        var profiling = profiler?.Enabled == true;
        long t0 = 0,
            t1 = 0,
            t2 = 0,
            t3 = 0,
            t4 = 0,
            t5 = 0,
            allocBefore = 0;
        var gen0Before = 0;
        if (profiling)
        {
            allocBefore = GC.GetAllocatedBytesForCurrentThread();
            gen0Before = GC.CollectionCount(0);
            t0 = Stopwatch.GetTimestamp();
        }

        canvas.Clear(new SKColor(theme.Background));

        if (profiling)
        {
            t1 = Stopwatch.GetTimestamp();
        }

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

        if (profiling)
        {
            t2 = Stopwatch.GetTimestamp();
        }

        // Pass 2: Collect all visible glyphs with positions, colors and style variants
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
                if (cell.blink)
                {
                    blinking = true;
                }
                if (cell.underline != UnderlineStyle.None || cell.strikethrough || cell.overline)
                {
                    decorated = true;
                }

                // Blank cells carry no glyph, SGR 8 conceals it, and a blinking cell drops it
                // for the off half of the blink cycle — but all three can still be decorated.
                if (cell.character <= ' ' || cell.invisible || (cell.blink && !blinkVisible))
                {
                    continue;
                }

                var glyphFont = GetFont(ResolveTypeface(cell.character), cell.bold, cell.italic);
                glyphBuf[count] = glyphFont.GetGlyph(cell.character);
                fontBuf[count] = glyphFont;
                posBuf[count] = new SKPoint(x * cellWidth, py);
                colorBuf[count] = GetFgColor(cell, x, y, selection);
                count++;
            }
        }

        HasBlinkingCells = blinking;

        if (profiling)
        {
            t3 = Stopwatch.GetTimestamp();
        }

        // Pass 3: Draw glyphs batched by color and font variant using SKTextBlob
        if (count > 0)
        {
            DrawGlyphsByColor(canvas, count);
        }

        // Pass 4: Decorations, drawn last so a strikethrough lands on top of its glyph.
        if (decorated)
        {
            for (var y = 0; y < buffer.rows; y++)
            {
                var row = buffer.GetRow(y);
                DrawRowDecorations(canvas, row, y, selection, blinkVisible, Decoration.Underline);
                DrawRowDecorations(canvas, row, y, selection, blinkVisible, Decoration.Strikeout);
                DrawRowDecorations(canvas, row, y, selection, blinkVisible, Decoration.Overline);
            }
        }

        if (profiling)
        {
            t4 = Stopwatch.GetTimestamp();
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
            if (cursorCell.character > ' ' && !cursorCell.invisible)
            {
                var cursorFont = GetFont(
                    ResolveTypeface(cursorCell.character),
                    cursorCell.bold,
                    cursorCell.italic
                );
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

        if (profiling)
        {
            t5 = Stopwatch.GetTimestamp();
        }

        if (overlays != null)
        {
            foreach (var overlay in overlays)
            {
                overlay.Render(canvas, canvasWidth, theme, font, typeface);
            }
        }

        if (profiling)
        {
            var tEnd = Stopwatch.GetTimestamp();
            profiler!.RecordFrame(
                clearTicks: t1 - t0,
                backgroundTicks: t2 - t1,
                glyphCollectTicks: t3 - t2,
                glyphDrawTicks: t4 - t3,
                cursorTicks: t5 - t4,
                overlayTicks: tEnd - t5,
                totalTicks: t5 - t0,
                allocatedBytesDelta: GC.GetAllocatedBytesForCurrentThread() - allocBefore,
                gen0CollectionsDelta: GC.CollectionCount(0) - gen0Before
            );
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
            var runFont = fontBuf[i];
            var runCount = 0;

            for (var j = i; j < count; j++)
            {
                if (!drawnBuf[j] && colorBuf[j] == color && ReferenceEquals(fontBuf[j], runFont))
                {
                    runGlyphBuf[runCount] = glyphBuf[j];
                    runPosBuf[runCount] = posBuf[j];
                    runCount++;
                    drawnBuf[j] = true;
                }
            }

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

    SKFont GetFont(SKTypeface? tf, bool bold, bool italic)
    {
        // The overwhelmingly common case: primary typeface, no synthetic styling.
        if (tf == null && !bold && !italic)
        {
            return font;
        }

        var key = (tf, bold, italic);
        if (styledFontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var variant = new SKFont(tf ?? typeface, font.Size)
        {
            Subpixel = true,
            Embolden = bold,
            SkewX = italic ? italicSkew : 0f,
        };
        styledFontCache[key] = variant;
        return variant;
    }

    enum Decoration
    {
        Underline,
        Strikeout,
        Overline,
    }

    /// <summary>
    /// Draws one decoration kind for a single row, batching contiguous cells that share a colour
    /// (and, for underlines, a style) into one stroke so a fully underlined line is a single draw.
    /// </summary>
    void DrawRowDecorations(
        SKCanvas canvas,
        ReadOnlySpan<Cell> row,
        int y,
        TextSelection? selection,
        bool blinkVisible,
        Decoration kind
    )
    {
        var top = y * cellHeight;
        var runStart = -1;
        var runColor = 0u;
        var runStyle = UnderlineStyle.None;

        // One past the end closes any run still open at the right edge of the row.
        for (var x = 0; x <= row.Length; x++)
        {
            var active = false;
            var color = 0u;
            var style = UnderlineStyle.None;

            if (x < row.Length)
            {
                var cell = row[x];
                if (blinkVisible || !cell.blink)
                {
                    switch (kind)
                    {
                        case Decoration.Underline:
                            style = cell.underline;
                            active = style != UnderlineStyle.None;
                            // 0 is the "no explicit SGR 58 colour" sentinel: inherit the text colour.
                            color =
                                cell.underlineColor != 0
                                    ? cell.underlineColor
                                    : GetFgColor(cell, x, y, selection);
                            break;
                        case Decoration.Strikeout:
                            active = cell.strikethrough;
                            color = GetFgColor(cell, x, y, selection);
                            break;
                        default:
                            active = cell.overline;
                            color = GetFgColor(cell, x, y, selection);
                            break;
                    }
                }
            }

            if (active && runStart >= 0 && color == runColor && style == runStyle)
            {
                continue;
            }

            if (runStart >= 0)
            {
                DrawDecoration(
                    canvas,
                    runStart * cellWidth,
                    x * cellWidth,
                    top,
                    runColor,
                    kind,
                    runStyle
                );
                runStart = -1;
            }

            if (active)
            {
                runStart = x;
                runColor = color;
                runStyle = style;
            }
        }
    }

    void DrawDecoration(
        SKCanvas canvas,
        float x0,
        float x1,
        float top,
        uint color,
        Decoration kind,
        UnderlineStyle style
    )
    {
        decorationPaint.Color = new SKColor(color);
        decorationPaint.Style = SKPaintStyle.Fill;

        if (kind == Decoration.Strikeout)
        {
            canvas.DrawRect(
                x0,
                top + strikethroughOffset,
                x1 - x0,
                decorationThickness,
                decorationPaint
            );
            return;
        }

        if (kind == Decoration.Overline)
        {
            canvas.DrawRect(
                x0,
                top + overlineOffset,
                x1 - x0,
                decorationThickness,
                decorationPaint
            );
            return;
        }

        var y = top + underlineOffset;
        switch (style)
        {
            case UnderlineStyle.Double:
                canvas.DrawRect(x0, y, x1 - x0, decorationThickness, decorationPaint);
                canvas.DrawRect(
                    x0,
                    y - decorationThickness * 2,
                    x1 - x0,
                    decorationThickness,
                    decorationPaint
                );
                break;
            case UnderlineStyle.Curly:
                DrawCurlyUnderline(canvas, x0, x1, y);
                break;
            case UnderlineStyle.Dotted:
                DrawDashedUnderline(canvas, x0, x1, y, decorationThickness, decorationThickness);
                break;
            case UnderlineStyle.Dashed:
                DrawDashedUnderline(
                    canvas,
                    x0,
                    x1,
                    y,
                    decorationThickness * 3,
                    decorationThickness * 2
                );
                break;
            default:
                canvas.DrawRect(x0, y, x1 - x0, decorationThickness, decorationPaint);
                break;
        }
    }

    /// <summary>Dots and dashes are drawn as pixel-aligned rects rather than via an
    /// SKPathEffect: no per-frame effect allocation, and the segments stay crisp.</summary>
    void DrawDashedUnderline(SKCanvas canvas, float x0, float x1, float y, float on, float off)
    {
        for (var x = x0; x < x1; x += on + off)
        {
            canvas.DrawRect(x, y, MathF.Min(on, x1 - x), decorationThickness, decorationPaint);
        }
    }

    void DrawCurlyUnderline(SKCanvas canvas, float x0, float x1, float baseY)
    {
        // A quadratic reaches half of its control-point offset, so a control offset of 2a gives
        // a wave of amplitude a — keeping the whole squiggle inside the cell.
        var amplitude = decorationThickness;
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

        decorationPaint.Style = SKPaintStyle.Stroke;
        decorationPaint.StrokeWidth = decorationThickness;
        canvas.DrawPath(curlyPath, decorationPaint);
        decorationPaint.Style = SKPaintStyle.Fill;
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
            fontBuf = new SKFont[cellCount];
            runGlyphBuf = new ushort[cellCount];
            runPosBuf = new SKPoint[cellCount];
        }
    }

    // SGR 7 and the selection highlight both swap foreground and background, so a selected
    // inverse cell inverts twice and renders as ordinary text — which is what users expect.
    static bool IsSwapped(Cell cell, int x, int y, TextSelection? selection) =>
        cell.inverse ^ (selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value));

    static uint GetFgColor(Cell cell, int x, int y, TextSelection? selection)
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

    static uint GetBgColor(Cell cell, int x, int y, TextSelection? selection) =>
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
        decorationPaint.Dispose();
        curlyPath.Dispose();
        // The primary font is never stored in this cache, so it can't be double-disposed here.
        foreach (var f in styledFontCache.Values)
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
