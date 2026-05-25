using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 4 / 10 / 11 / 104 — palette and default fg/bg color set &amp; query.
///
/// Ported from ghostty/src/terminal/osc/parsers/color.zig (tests "OSC 4:",
/// "OSC 4: multiple requests", "OSC 104:", and the "OSC 10:..OSC 11:.. dynamic"
/// group). Colors use the X11 "rgb:rr/gg/bb" spec; a "?" value is a query that
/// must be answered on the response channel.
///
/// Intended API (not yet implemented):
///   VtParser: uint[] Palette; uint DefaultForeground; uint DefaultBackground;
///   queries emit a reply on the Respond channel (format "rgb:rrrr/gggg/bbbb").
/// </summary>
public class VtParserOscColorTests
{
    readonly VtParser parser;

    public VtParserOscColorTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
    }

    [Fact]
    public void Osc4_SetPaletteColor()
    {
        parser.Send("\x1b]4;1;rgb:ff/00/00\a");
        Assert.Equal(0xFFFF0000u, parser.Palette[1]);
    }

    [Fact]
    public void Osc4_SingleHexDigitChannel_ScalesToFullByte()
    {
        // X11 allows 1-4 hex digits per channel, each scaled so the max for its
        // width maps to 0xff. A 1-digit 'f' is 4-bit full intensity and must
        // become 0xff (nibble replication), not 0x0f.
        parser.Send("\x1b]4;1;rgb:f/0/0\a");
        Assert.Equal(0xFFFF0000u, parser.Palette[1]);
    }

    [Fact]
    public void Osc4_FourHexDigitChannel_ScalesToEightBits()
    {
        // 16-bit channels: ffff -> 0xff, 0000 -> 0x00, 8000 -> ~0x80 (proportional).
        parser.Send("\x1b]4;2;rgb:ffff/0000/8000\a");
        Assert.Equal(0xFFFF0080u, parser.Palette[2]);
    }

    [Fact]
    public void Osc4_QueryPaletteColor_EmitsResponse()
    {
        var responses = TerminalTestHelpers.CaptureResponses(parser);
        parser.Send("\x1b]4;1;?\a");
        Assert.NotEmpty(responses);
    }

    [Fact]
    public void Osc10_SetDefaultForeground()
    {
        parser.Send("\x1b]10;rgb:ff/ff/ff\a");
        Assert.Equal(0xFFFFFFFFu, parser.DefaultForeground);
    }

    [Fact]
    public void Osc11_SetDefaultBackground()
    {
        parser.Send("\x1b]11;rgb:00/00/00\a");
        Assert.Equal(0xFF000000u, parser.DefaultBackground);
    }

    [Fact]
    public void Osc10_QueryDefaultForeground_EmitsResponse()
    {
        var responses = TerminalTestHelpers.CaptureResponses(parser);
        parser.Send("\x1b]10;?\a");
        Assert.NotEmpty(responses);
    }

    [Fact]
    public void Osc104_ResetPalette_RestoresThemeColor()
    {
        var original = parser.Palette[1];
        parser.Send("\x1b]4;1;rgb:12/34/56\a");
        Assert.NotEqual(original, parser.Palette[1]);

        parser.Send("\x1b]104;1\a");
        Assert.Equal(original, parser.Palette[1]);
    }
}
