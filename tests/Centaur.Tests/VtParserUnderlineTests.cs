using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Underline styles and underline color.
///
/// Ported from ghostty/src/terminal/sgr.zig: tests "sgr: underline",
/// "sgr: underline styles", "sgr: underline color", "sgr: reset underline
/// color", "sgr: kakoune input", "sgr: kakoune input issue underline, fg,
/// and bg". The colon sub-parameter form (ESC[4:3m) selects the style;
/// SGR 21 is double underline, SGR 24 removes it; SGR 58 sets the underline
/// color, SGR 59 resets it.
///
/// Intended API (not yet implemented): enum UnderlineStyle
/// { None=0, Single=1, Double=2, Curly=3, Dotted=4, Dashed=5 }, Cell.underline
/// of that type, and Cell.underlineColor (uint ARGB; 0 = inherit foreground).
/// </summary>
public class VtParserUnderlineTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserUnderlineTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Underline_Code4_IsSingle()
    {
        parser.Send("\x1b[4mX");
        Assert.Equal(UnderlineStyle.Single, buffer[0, 0].underline);
    }

    [Fact]
    public void Underline_Code24_RemovesUnderline()
    {
        parser.Send("\x1b[4m\x1b[24mX");
        Assert.Equal(UnderlineStyle.None, buffer[0, 0].underline);
    }

    [Fact]
    public void Underline_Code21_IsDouble()
    {
        parser.Send("\x1b[21mX");
        Assert.Equal(UnderlineStyle.Double, buffer[0, 0].underline);
    }

    [Theory]
    [InlineData(0, UnderlineStyle.None)]
    [InlineData(1, UnderlineStyle.Single)]
    [InlineData(2, UnderlineStyle.Double)]
    [InlineData(3, UnderlineStyle.Curly)]
    [InlineData(4, UnderlineStyle.Dotted)]
    [InlineData(5, UnderlineStyle.Dashed)]
    public void Underline_ColonStyle_SelectsStyle(int code, UnderlineStyle expected)
    {
        // ESC[4:Nm — colon sub-parameter selects the underline style.
        parser.Send($"\x1b[4:{code}mX");
        Assert.Equal(expected, buffer[0, 0].underline);
    }

    [Fact]
    public void UnderlineColor_TrueColor_Code58_2()
    {
        // 58;2;r;g;b sets underline color directly.
        parser.Send("\x1b[58;2;1;2;3mX");
        Assert.Equal(0xFF010203u, buffer[0, 0].underlineColor);
    }

    [Fact]
    public void UnderlineColor_Indexed_Code58_5()
    {
        // 58;5;n sets underline color from the palette.
        parser.Send("\x1b[58;5;9mX");
        Assert.Equal(theme.GetColor(9), buffer[0, 0].underlineColor);
    }

    [Fact]
    public void UnderlineColor_Reset_Code59_InheritsForeground()
    {
        parser.Send("\x1b[58;2;1;2;3m\x1b[59mX");
        // 0 is the sentinel meaning "inherit the foreground color".
        Assert.Equal(0u, buffer[0, 0].underlineColor);
    }

    [Fact]
    public void Underline_KakouneCurlyWithUnderlineColor()
    {
        // Ported from "sgr: kakoune input issue underline, fg, and bg":
        // \033[4:3;38;2;51;51;51;48;2;170;170;170;58;2;255;97;136m
        parser.Send("\x1b[4:3;38;2;51;51;51;48;2;170;170;170;58;2;255;97;136mX");
        var cell = buffer[0, 0];
        Assert.Equal(UnderlineStyle.Curly, cell.underline);
        Assert.Equal(0xFF333333u, cell.foreground);
        Assert.Equal(0xFFAAAAAAu, cell.background);
        Assert.Equal(0xFFFF6188u, cell.underlineColor);
    }
}
