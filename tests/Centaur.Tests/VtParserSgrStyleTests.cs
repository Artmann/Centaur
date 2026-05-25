using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// SGR text-style attributes (bold/faint/italic/blink/inverse/invisible/
/// strikethrough/overline) stored on the cell.
///
/// Ported from ghostty/src/terminal/sgr.zig: tests "sgr: bold", "sgr: italic",
/// "sgr: blink", "sgr: inverse", "sgr: strikethrough", "sgr: invisible",
/// "sgr: underline, bg, and fg". In Ghostty these assert on the standalone
/// sgr.Parser yielding Attribute values; here we assert the resulting Cell
/// carries the style, which is what Centaur ultimately needs to render.
///
/// Intended API (not yet implemented): Cell gains bool fields
/// bold/faint/italic/blink/inverse/invisible/strikethrough/overline.
/// </summary>
public class VtParserSgrStyleTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserSgrStyleTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Sgr_Bold_SetsBold()
    {
        parser.Send("\x1b[1mX");
        Assert.True(buffer[0, 0].bold);
    }

    [Fact]
    public void Sgr_ResetBold_Code22_ClearsBold()
    {
        parser.Send("\x1b[1m\x1b[22mX");
        Assert.False(buffer[0, 0].bold);
    }

    [Fact]
    public void Sgr_Faint_SetsFaint()
    {
        parser.Send("\x1b[2mX");
        Assert.True(buffer[0, 0].faint);
    }

    [Fact]
    public void Sgr_Italic_SetsItalic()
    {
        parser.Send("\x1b[3mX");
        Assert.True(buffer[0, 0].italic);
    }

    [Fact]
    public void Sgr_ResetItalic_Code23_ClearsItalic()
    {
        parser.Send("\x1b[3m\x1b[23mX");
        Assert.False(buffer[0, 0].italic);
    }

    [Fact]
    public void Sgr_Blink_Code5_SetsBlink()
    {
        parser.Send("\x1b[5mX");
        Assert.True(buffer[0, 0].blink);
    }

    [Fact]
    public void Sgr_RapidBlink_Code6_SetsBlink()
    {
        // Ghostty treats 5 and 6 identically as blink.
        parser.Send("\x1b[6mX");
        Assert.True(buffer[0, 0].blink);
    }

    [Fact]
    public void Sgr_ResetBlink_Code25_ClearsBlink()
    {
        parser.Send("\x1b[5m\x1b[25mX");
        Assert.False(buffer[0, 0].blink);
    }

    [Fact]
    public void Sgr_Inverse_Code7_SetsInverse()
    {
        parser.Send("\x1b[7mX");
        Assert.True(buffer[0, 0].inverse);
    }

    [Fact]
    public void Sgr_ResetInverse_Code27_ClearsInverse()
    {
        parser.Send("\x1b[7m\x1b[27mX");
        Assert.False(buffer[0, 0].inverse);
    }

    [Fact]
    public void Sgr_Invisible_Code8_SetsInvisible()
    {
        parser.Send("\x1b[8mX");
        Assert.True(buffer[0, 0].invisible);
    }

    [Fact]
    public void Sgr_ResetInvisible_Code28_ClearsInvisible()
    {
        parser.Send("\x1b[8m\x1b[28mX");
        Assert.False(buffer[0, 0].invisible);
    }

    [Fact]
    public void Sgr_Strikethrough_Code9_SetsStrikethrough()
    {
        parser.Send("\x1b[9mX");
        Assert.True(buffer[0, 0].strikethrough);
    }

    [Fact]
    public void Sgr_ResetStrikethrough_Code29_ClearsStrikethrough()
    {
        parser.Send("\x1b[9m\x1b[29mX");
        Assert.False(buffer[0, 0].strikethrough);
    }

    [Fact]
    public void Sgr_Overline_Code53_SetsOverline()
    {
        parser.Send("\x1b[53mX");
        Assert.True(buffer[0, 0].overline);
    }

    [Fact]
    public void Sgr_ResetOverline_Code55_ClearsOverline()
    {
        parser.Send("\x1b[53m\x1b[55mX");
        Assert.False(buffer[0, 0].overline);
    }

    [Fact]
    public void Sgr_Reset_Code0_ClearsAllStyles()
    {
        parser.Send("\x1b[1;3;4;5;7;9mX\x1b[0mY");
        var y = buffer[1, 0];
        Assert.False(y.bold);
        Assert.False(y.italic);
        Assert.Equal(UnderlineStyle.None, y.underline);
        Assert.False(y.blink);
        Assert.False(y.inverse);
        Assert.False(y.strikethrough);
    }

    [Fact]
    public void Sgr_StylesPersistAcrossCharacters()
    {
        parser.Send("\x1b[1mABC");
        Assert.True(buffer[0, 0].bold);
        Assert.True(buffer[1, 0].bold);
        Assert.True(buffer[2, 0].bold);
    }

    [Fact]
    public void Sgr_CombinedStyleAndColors_AllApplied()
    {
        // Ported from "sgr: underline, bg, and fg":
        // underline + 24-bit fg (255,247,219) + 24-bit bg (242,93,147).
        parser.Send("\x1b[4;38;2;255;247;219;48;2;242;93;147mX");
        var cell = buffer[0, 0];
        Assert.Equal(UnderlineStyle.Single, cell.underline);
        Assert.Equal(0xFFFFF7DBu, cell.foreground);
        Assert.Equal(0xFFF25D93u, cell.background);
    }
}
