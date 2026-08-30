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

    // Matched typefaces get their own SKFont sized identically to the primary font.
    // Only ever touched on the render thread (GetFont), so a plain Dictionary is fine.
    readonly Dictionary<SKTypeface, SKFont> fontCache = new();

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

    /// <summary>The font to draw with for a typeface from <see cref="ResolveTypeface"/>.</summary>
    public SKFont GetFont(SKTypeface? tf)
    {
        if (tf == null)
        {
            return primaryFont;
        }

        if (fontCache.TryGetValue(tf, out var cached))
        {
            return cached;
        }

        var matched = new SKFont(tf, primaryFont.Size) { Subpixel = true };
        fontCache[tf] = matched;
        return matched;
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
