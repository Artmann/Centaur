using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Font fallback for codepoints the embedded primary font lacks must be resolved on a
/// background thread, never synchronously on the UI/render thread. A synchronous
/// SKFontManager.MatchCharacter call froze the UI during Claude Code's startup (~10s
/// input lag that warmed up as the per-codepoint cache filled). Render must return
/// promptly and the resolver must populate the cache out of band.
/// </summary>
public class TerminalRendererFallbackTests
{
    // CJK ideograph U+4E2D — a BMP char (fits in System.Char) that JetBrains Mono,
    // the embedded primary font, does not cover, so it forces the fallback path.
    const char uncoveredGlyph = '中';

    [Fact]
    public void Render_UncoveredGlyph_ResolvesFallbackOffThread()
    {
        var theme = CatppuccinThemes.Macchiato;
        using var renderer = new TerminalRenderer(theme);
        var buffer = new ScreenBuffer(4, 1, theme);
        buffer.Write(uncoveredGlyph);

        using var surface = SKSurface.Create(new SKImageInfo(64, 32));

        // Must not throw and must return (rather than block on a slow system font scan).
        renderer.Render(surface.Canvas, buffer, 64f);

        // The resolver fills the cache out of band — for primary-covered glyphs it would
        // be filled synchronously, but an uncovered glyph proves the background path runs.
        var resolved = SpinUntil(() => renderer.fallbackTypefaceCache.ContainsKey(uncoveredGlyph));

        Assert.True(resolved, "fallback resolver did not populate the cache for the glyph");
    }

    static bool SpinUntil(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }
            Thread.Sleep(10);
        }
        return condition();
    }
}
