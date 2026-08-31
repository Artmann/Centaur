using System.Collections.Concurrent;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Finds a typeface for codepoints the primary font lacks a glyph for (box-drawing, dingbats,
/// colour emoji). The system lookup (<c>SKFontManager.MatchCharacter</c>) is slow enough to
/// freeze the UI thread, so it runs on a background thread; the render thread only does the
/// cheap primary-coverage check and reads the cache, drawing with the primary font until the
/// resolver answers. The continuous animation loop repaints once it does.
/// </summary>
internal sealed class FontFallbackResolver : IDisposable
{
    readonly SKFont primaryFont;

    // codepoint -> matched fallback typeface (null = primary covers it, or no match found).
    // Read on the render thread, written by the resolver, so it must be concurrent.
    readonly ConcurrentDictionary<char, SKTypeface?> cache = new();

    // Codepoints already queued for background resolution, so each is enqueued at most once.
    readonly ConcurrentDictionary<char, byte> pending = new();
    readonly BlockingCollection<char> queue = new();
    readonly Thread resolver;

    // One SKFont per (typeface, bold, italic) combination, sized identically to the primary
    // font. Only JetBrains Mono Regular is embedded, so bold and italic are synthesised via
    // Embolden/SkewX - that keeps the typeface (and therefore every glyph id) unchanged across
    // variants, which is what lets a single glyph buffer serve all of them.
    // Only ever touched on the render thread (GetFont), so a plain Dictionary is fine.
    readonly Dictionary<(SKTypeface? typeface, bool bold, bool italic), SKFont> fontCache = new();

    // Horizontal shear applied for synthetic italics: negative leans the top of the glyph right.
    const float italicSkew = -0.22f;

    public FontFallbackResolver(SKFont primaryFont)
    {
        this.primaryFont = primaryFont;
        resolver = new Thread(ResolveLoop) { IsBackground = true, Name = "font-fallback-resolver" };
        resolver.Start();
    }

    /// <summary>Test hook: has the background resolver answered for this codepoint yet?</summary>
    internal bool IsResolved(char c) => cache.ContainsKey(c);

    /// <summary>The typeface to draw <paramref name="c"/> with, or null for the primary font.
    /// Never blocks: an unresolved codepoint is queued and falls back to the primary font.</summary>
    public SKTypeface? ResolveTypeface(char c)
    {
        if (cache.TryGetValue(c, out var cached))
        {
            return cached;
        }

        // Cheap, primary-font-only check - never touches SKFontManager.
        if (primaryFont.GetGlyph(c) != 0)
        {
            cache[c] = null; // primary font covers it
            return null;
        }

        if (pending.TryAdd(c, 0))
        {
            queue.Add(c);
        }
        return null;
    }

    /// <summary>The font to draw with for a typeface from <see cref="ResolveTypeface"/>, in the
    /// synthetic bold/italic variant the cell's SGR attributes ask for.</summary>
    public SKFont GetFont(SKTypeface? tf, bool bold = false, bool italic = false)
    {
        // The overwhelmingly common case: primary typeface, no synthetic styling.
        if (tf == null && !bold && !italic)
        {
            return primaryFont;
        }

        var key = (tf, bold, italic);
        if (fontCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var variant = new SKFont(tf ?? primaryFont.Typeface, primaryFont.Size)
        {
            Subpixel = true,
            Embolden = bold,
            SkewX = italic ? italicSkew : 0f,
        };
        fontCache[key] = variant;
        return variant;
    }

    void ResolveLoop()
    {
        try
        {
            foreach (var c in queue.GetConsumingEnumerable())
            {
                try
                {
                    // The first call here also forces SKFontManager.Default's one-time
                    // system font collection init off the UI thread.
                    cache[c] = SKFontManager.Default.MatchCharacter(c);
                }
                catch
                {
                    // Give up gracefully on this codepoint; it draws with the primary
                    // font (tofu), which is the visible signal that no glyph was found.
                    cache[c] = null;
                }
            }
        }
        catch (ObjectDisposedException)
        {
            // Queue disposed during shutdown - expected.
        }
        catch (InvalidOperationException)
        {
            // GetConsumingEnumerable after CompleteAdding - expected on shutdown.
        }
    }

    public void Dispose()
    {
        queue.CompleteAdding();
        resolver.Join(TimeSpan.FromSeconds(1));
        queue.Dispose();

        // The primary font is never stored in this cache, so it can't be double-disposed here.
        foreach (var f in fontCache.Values)
        {
            f.Dispose();
        }

        // The resolver thread has been joined, so the cache is now stable. Dispose the
        // distinct system typefaces it resolved (never the primary one, which the renderer
        // owns) to avoid leaking native handles in long sessions.
        var disposed = new HashSet<SKTypeface>();
        foreach (var fallback in cache.Values)
        {
            if (fallback != null && fallback != primaryFont.Typeface && disposed.Add(fallback))
            {
                fallback.Dispose();
            }
        }
    }
}
