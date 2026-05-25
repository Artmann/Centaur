using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 8 — hyperlinks.
///
/// Ported from ghostty/src/terminal/osc/parsers/hyperlink.zig (tests
/// "OSC 8: hyperlink", "...with id set", "...with empty id",
/// "...with empty uri", "OSC 8: hyperlink end"). Format is
/// "8;{params};{uri}"; an empty URI ends the current hyperlink. Cells printed
/// while a hyperlink is active carry its URI so the renderer can make them
/// clickable.
///
/// Intended API (not yet implemented): Cell gains string? hyperlink, set to
/// the active OSC 8 URI (null when none).
/// </summary>
public class VtParserHyperlinkTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;

    public VtParserHyperlinkTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Osc8_Hyperlink_AppliesUriToPrintedCells()
    {
        parser.Send("\x1b]8;;http://example.com\aX");
        Assert.Equal("http://example.com", buffer[0, 0].hyperlink);
    }

    [Fact]
    public void Osc8_HyperlinkWithId_StillAppliesUri()
    {
        parser.Send("\x1b]8;id=foo;http://example.com\aX");
        Assert.Equal("http://example.com", buffer[0, 0].hyperlink);
    }

    [Fact]
    public void Osc8_HyperlinkWithEmptyId_StillAppliesUri()
    {
        parser.Send("\x1b]8;id=;http://example.com\aX");
        Assert.Equal("http://example.com", buffer[0, 0].hyperlink);
    }

    [Fact]
    public void Osc8_End_StopsApplyingUri()
    {
        // Start, print 'A', end, print 'B'. Only 'A' is linked.
        parser.Send("\x1b]8;;http://example.com\aA\x1b]8;;\aB");
        Assert.Equal("http://example.com", buffer[0, 0].hyperlink);
        Assert.Null(buffer[1, 0].hyperlink);
    }

    [Fact]
    public void NoHyperlink_CellsHaveNullLink()
    {
        parser.Send("X");
        Assert.Null(buffer[0, 0].hyperlink);
    }
}
