using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// SGR text styles are parsed into <see cref="Cell"/> by <see cref="VtParser"/> but were
/// dropped by the renderer, so bold/italic/inverse/underline all drew as plain text. These
/// tests drive real SGR sequences through the parser and then sample pixels from the real
/// <see cref="TerminalRenderer"/> (via <see cref="RenderProbe"/>) to assert each style is
/// actually painted.
///
/// The cursor is disabled in every render so its inverted block never contaminates a sample.
/// </summary>
public class TerminalRendererStyleTests
{
    const string esc = "\u001b";

    static readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    static SKColor Background => RenderProbe.ToColor(theme.Background);
    static SKColor Foreground => RenderProbe.ToColor(theme.Foreground);

    static (ScreenBuffer buffer, VtParser parser) NewTerminal(int columns = 8, int rows = 2)
    {
        var buffer = new ScreenBuffer(columns, rows, theme, enableScrollback: false);
        return (buffer, new VtParser(buffer, theme));
    }

    static SKBitmap Render(ScreenBuffer buffer, TerminalRenderer renderer) =>
        RenderProbe.RenderToBitmap(buffer, renderer, cursorVisible: false);

    // Ink pixels in the horizontal band [fromFraction, toFraction) of a cell's height.
    static int InkInBand(
        SKBitmap bitmap,
        TerminalRenderer renderer,
        int col,
        int row,
        float fromFraction,
        float toFraction
    )
    {
        var x0 = (int)(col * renderer.cellWidth);
        var x1 = Math.Min((int)Math.Ceiling((col + 1) * renderer.cellWidth), bitmap.Width);
        var top = row * renderer.cellHeight;
        var y0 = Math.Max(0, (int)(top + fromFraction * renderer.cellHeight));
        var y1 = Math.Min(bitmap.Height, (int)(top + toFraction * renderer.cellHeight));

        var count = 0;
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (!RenderProbe.ColorsClose(bitmap.GetPixel(x, y), Background))
                {
                    count++;
                }
            }
        }
        return count;
    }

    static int Distance(SKColor a, SKColor b) =>
        Math.Abs(a.Red - b.Red) + Math.Abs(a.Green - b.Green) + Math.Abs(a.Blue - b.Blue);

    [Fact]
    public void Inverse_SwapsForegroundAndBackground()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[7mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        // The whole cell is painted in the (previous) foreground colour...
        var filled = RenderProbe.CountPixelsClose(bitmap, renderer, 0, 0, Foreground);
        Assert.True(filled > 0, "inverse cell did not paint the foreground colour as background");

        // ...and the glyph itself is punched out in the background colour.
        var glyph = RenderProbe.CountPixelsClose(bitmap, renderer, 0, 0, Background);
        Assert.True(glyph > 0, "inverse cell did not draw the glyph in the background colour");
    }

    [Fact]
    public void Inverse_CancelsOutUnderSelection()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[7mX");

        using var renderer = new TerminalRenderer(theme);
        // End column is exclusive, so this selects exactly column 0 of row 0.
        var selection = new TextSelection(0, 0, 1, 0);
        var width = (int)Math.Ceiling(buffer.columns * renderer.cellWidth);
        var height = (int)Math.Ceiling(buffer.rows * renderer.cellHeight);
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888));
        using (var canvas = new SKCanvas(bitmap))
        {
            renderer.Render(canvas, buffer, width, selection, cursorVisible: false);
        }

        // Selection inverts, and SGR 7 inverts again — the two cancel, so the cell renders as
        // normal text on the theme background rather than as a solid block.
        var backgroundPixels = RenderProbe.CountPixelsClose(bitmap, renderer, 0, 0, Background);
        var inkPixels = RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, Background);
        Assert.True(backgroundPixels > inkPixels, "inverse + selection did not cancel out");
    }

    [Fact]
    public void Invisible_DrawsNoGlyph()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[8mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var ink = RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, Background);
        Assert.Equal(0, ink);
    }

    [Fact]
    public void Bold_DrawsMoreInkThanRegular()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send("X" + esc + "[1mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var regular = RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, Background);
        var bold = RenderProbe.ForegroundPixelCount(bitmap, renderer, 1, 0, Background);

        Assert.True(regular > 0, "regular glyph drew no ink");
        Assert.True(bold > regular, $"bold ({bold}) should out-ink regular ({regular})");
    }

    [Fact]
    public void Italic_RendersDifferentlyFromRegular()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send("X" + esc + "[3mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        // Compare the top half of each glyph: a skew shifts the upper strokes horizontally,
        // so the two cells cannot be pixel-identical.
        var identical = true;
        var x0Italic = (int)renderer.cellWidth;
        var span = (int)renderer.cellWidth;
        var rows = (int)(renderer.cellHeight / 2);
        for (var y = 0; y < rows && identical; y++)
        {
            for (var dx = 0; dx < span; dx++)
            {
                var a = bitmap.GetPixel(dx, y);
                var b = bitmap.GetPixel(x0Italic + dx, y);
                if (!RenderProbe.ColorsClose(a, b, 8))
                {
                    identical = false;
                    break;
                }
            }
        }

        Assert.False(identical, "italic rendered pixel-identically to regular");
    }

    [Fact]
    public void Faint_DrawsDimmerInkThanRegular()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send("X" + esc + "[2mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var regular = RenderProbe.DominantForegroundColor(bitmap, renderer, 0, 0, Background);
        var faint = RenderProbe.DominantForegroundColor(bitmap, renderer, 1, 0, Background);

        // Faint blends toward the background, so its ink sits closer to it than regular ink.
        Assert.True(
            Distance(faint, Background) < Distance(regular, Background),
            $"faint ink {faint} was not dimmer than regular ink {regular}"
        );
    }

    [Fact]
    public void Underline_DrawsInkBelowTheGlyph()
    {
        var (buffer, parser) = NewTerminal();
        // A space carries no glyph, so any ink in the cell must be the underline itself.
        parser.Send(esc + "[4m ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var lower = InkInBand(bitmap, renderer, 0, 0, 0.6f, 1.0f);
        Assert.True(lower > 0, "single underline drew no ink in the lower part of the cell");
    }

    [Fact]
    public void DoubleUnderline_DrawsMoreInkThanSingle()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[4m " + esc + "[4:2m ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var single = RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, Background);
        var doubled = RenderProbe.ForegroundPixelCount(bitmap, renderer, 1, 0, Background);

        Assert.True(single > 0, "single underline drew no ink");
        Assert.True(doubled > single, $"double ({doubled}) should out-ink single ({single})");
    }

    [Theory]
    [InlineData("[4:3m")] // curly
    [InlineData("[4:4m")] // dotted
    [InlineData("[4:5m")] // dashed
    public void UnderlineVariants_DrawInk(string sgr)
    {
        var (buffer, parser) = NewTerminal();
        // Four spaces so dotted/dashed have room for at least one "on" segment.
        parser.Send(esc + sgr + "    ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var ink = 0;
        for (var col = 0; col < 4; col++)
        {
            ink += InkInBand(bitmap, renderer, col, 0, 0.5f, 1.0f);
        }

        Assert.True(ink > 0, $"underline variant {sgr} drew no ink");
    }

    [Fact]
    public void UnderlineColor_UsesTheExplicitColour()
    {
        var (buffer, parser) = NewTerminal();
        // SGR 58 sets the underline colour independently of the foreground: pure red here.
        parser.Send(esc + "[4m" + esc + "[58;2;255;0;0m ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var ink = RenderProbe.DominantForegroundColor(bitmap, renderer, 0, 0, Background);
        Assert.True(
            ink.Red > ink.Green + 60 && ink.Red > ink.Blue + 60,
            $"expected a red underline but drew {ink}"
        );
    }

    [Fact]
    public void Strikethrough_DrawsInkAcrossTheMiddle()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[9m ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var middle = InkInBand(bitmap, renderer, 0, 0, 0.3f, 0.75f);
        Assert.True(middle > 0, "strikethrough drew no ink across the middle of the cell");
    }

    [Fact]
    public void Overline_DrawsInkAtTheTop()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[53m ");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        var top = InkInBand(bitmap, renderer, 0, 0, 0.0f, 0.35f);
        Assert.True(top > 0, "overline drew no ink at the top of the cell");
    }

    [Fact]
    public void Blink_HidesTheGlyphOnTheOffPhase()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[5mX");

        using var renderer = new TerminalRenderer(theme);
        var width = (int)Math.Ceiling(buffer.columns * renderer.cellWidth);
        var height = (int)Math.Ceiling(buffer.rows * renderer.cellHeight);

        using var onBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888));
        using (var canvas = new SKCanvas(onBitmap))
        {
            renderer.Render(canvas, buffer, width, cursorVisible: false, blinkVisible: true);
        }

        using var offBitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888));
        using (var canvas = new SKCanvas(offBitmap))
        {
            renderer.Render(canvas, buffer, width, cursorVisible: false, blinkVisible: false);
        }

        Assert.True(
            RenderProbe.ForegroundPixelCount(onBitmap, renderer, 0, 0, Background) > 0,
            "blinking glyph was not drawn on the visible phase"
        );
        Assert.Equal(0, RenderProbe.ForegroundPixelCount(offBitmap, renderer, 0, 0, Background));
    }

    [Fact]
    public void Blink_IsReportedSoTheControlKeepsAnimating()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send(esc + "[5mX");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        Assert.True(renderer.HasBlinkingCells, "renderer did not report the blinking cell");
    }

    [Fact]
    public void NoBlinkingCells_IsReportedSoTheTerminalCanIdle()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send("plain");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        Assert.False(renderer.HasBlinkingCells, "renderer reported blinking on plain text");
    }

    [Fact]
    public void PlainText_IsUnaffected()
    {
        var (buffer, parser) = NewTerminal();
        parser.Send("X");

        using var renderer = new TerminalRenderer(theme);
        using var bitmap = Render(buffer, renderer);

        // The sampled ink is an average over antialiased edge pixels, so it never lands exactly
        // on the foreground — assert it leans that way rather than pinning an exact colour.
        var ink = RenderProbe.DominantForegroundColor(bitmap, renderer, 0, 0, Background);
        Assert.True(
            Distance(ink, Foreground) < Distance(ink, Background),
            $"plain text rendered {ink}, closer to the background than to {Foreground}"
        );
    }
}
