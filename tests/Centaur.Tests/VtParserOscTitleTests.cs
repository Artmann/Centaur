using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 0 / 1 / 2 — window title and icon name.
///
/// Ported from ghostty/src/terminal/osc/parsers/change_window_title.zig
/// (tests "OSC 0: change_window_title", "OSC 2: change_window_title with 2",
/// "OSC 2: change_window_title with utf8", "OSC 2: change_window_title empty")
/// and change_window_icon.zig ("OSC 1: change_window_icon").
///
/// OSC 0 sets both window title and icon; OSC 2 sets only the title; OSC 1
/// sets only the icon. Either BEL (0x07) or ST (ESC \) terminates.
///
/// Intended API (not yet implemented): VtParser exposes
/// string? WindowTitle and string? IconName, set as OSC sequences arrive
/// (replacing today's silent discard).
/// </summary>
public class VtParserOscTitleTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;

    public VtParserOscTitleTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Osc0_SetsWindowTitle()
    {
        parser.Send("\x1b]0;ab\a");
        Assert.Equal("ab", parser.WindowTitle);
    }

    [Fact]
    public void Osc2_SetsWindowTitle()
    {
        parser.Send("\x1b]2;ab\a");
        Assert.Equal("ab", parser.WindowTitle);
    }

    [Fact]
    public void Osc2_StTerminator_SetsWindowTitle()
    {
        parser.Send("\x1b]2;ab\x1b\\");
        Assert.Equal("ab", parser.WindowTitle);
    }

    [Fact]
    public void Osc2_Utf8Title()
    {
        // "— ‐" : EM DASH U+2014 (E2 80 94), space, HYPHEN U+2010 (E2 80 90).
        parser.Send("\x1b]2;— ‐\a");
        Assert.Equal("— ‐", parser.WindowTitle);
    }

    [Fact]
    public void Osc2_EmptyTitle()
    {
        parser.Send("\x1b]2;\a");
        Assert.Equal("", parser.WindowTitle);
    }

    [Fact]
    public void Osc1_SetsIconName()
    {
        parser.Send("\x1b]1;myicon\a");
        Assert.Equal("myicon", parser.IconName);
    }

    [Fact]
    public void Osc_DoesNotEmitVisibleOutput()
    {
        // The title sequence itself must not print to the grid.
        parser.Send("\x1b]0;title\ahi");
        Assert.Equal('h', buffer[0, 0].character);
        Assert.Equal('i', buffer[1, 0].character);
    }
}
