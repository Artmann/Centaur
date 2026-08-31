using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The reported artifact, end to end through the real renderer: solid green rectangles with
/// no glyph in them, left behind where a diff had been drawn.
///
/// A blank cell only paints as a solid block when its resolved background is not the theme's,
/// and inverse is what makes a cell resolve its *foreground* as the background
/// (<see cref="CellColors"/>). So a run of spaces printed with a green pen and inverse stuck
/// on is exactly a green rectangle. The first test drives the sequence that used to leave
/// inverse stuck; the second is its control, confirming the probe would have seen the block.
/// </summary>
public class InverseArtifactRenderTests
{
    static readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    // \x is a variable-length escape in C#, so "\x1b7" would be U+01B7 rather than ESC 7.
    const char esc = '\x1b';
    static readonly string decsc = $"{esc}7";
    static readonly string decrc = $"{esc}8";

    const string padding = "          ";

    [Fact]
    public void SpacesAfterASaveRestorePair_RenderAsThemeBackground()
    {
        using var frame = Draw($"\x1b[32m{decsc}\x1b[7m{decrc}{padding}");

        AssertRowIs(frame, theme.Background, "the restore left inverse stuck on the pen");
    }

    // The same run with inverse deliberately on: the block is real, and the probe sees it.
    [Fact]
    public void SpacesPrintedWithInverse_RenderAsTheForegroundColor()
    {
        using var frame = Draw($"\x1b[32;7m{padding}");

        AssertRowIs(frame, theme.GetColor(2), "an inverse run should paint its foreground");
    }

    static void AssertRowIs(RenderedFrame frame, uint expected, string because)
    {
        var wanted = RenderProbe.ToColor(expected);
        for (var col = 0; col < padding.Length; col++)
        {
            var actual = RenderProbe.CellCenterPixel(frame.Bitmap, frame.Renderer, col, 0);
            Assert.True(
                RenderProbe.ColorsClose(actual, wanted),
                $"column {col} rendered {actual}, expected {wanted} - {because}"
            );
        }
    }

    static RenderedFrame Draw(string sequence)
    {
        var buffer = new ScreenBuffer(padding.Length + 4, 3, theme);
        var parser = new VtParser(buffer, theme);
        parser.Send(sequence);

        var renderer = new TerminalRenderer(theme);
        return new RenderedFrame
        {
            Renderer = renderer,
            Bitmap = RenderProbe.RenderToBitmap(buffer, renderer, cursorVisible: false),
        };
    }

    sealed class RenderedFrame : IDisposable
    {
        public required TerminalRenderer Renderer { get; init; }
        public required SKBitmap Bitmap { get; init; }

        public void Dispose()
        {
            Bitmap.Dispose();
            Renderer.Dispose();
        }
    }
}
