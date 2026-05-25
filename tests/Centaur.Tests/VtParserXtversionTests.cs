using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// XTVERSION — CSI &gt; q — asks the terminal to identify itself. Claude Code emits this
/// at startup (CSI &gt; 0 q) and blocks on a ~5s timeout if it goes unanswered, stalling
/// before it will echo input. The terminal must reply with a DCS &gt; | name version ST
/// string on the Respond channel.
/// </summary>
public class VtParserXtversionTests
{
    readonly VtParser parser;
    readonly List<string> responses;

    public VtParserXtversionTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
        responses = TerminalTestHelpers.CaptureResponses(parser);
    }

    [Fact]
    public void Xtversion_RepliesDcsVersionString()
    {
        // CSI > 0 q -> DCS > | Centaur(version) ST.
        parser.Send("\x1b[>0q");
        Assert.Equal("\x1bP>|Centaur(0.1.0)\x1b\\", Assert.Single(responses));
    }

    [Fact]
    public void Xtversion_NoParam_StillReplies()
    {
        parser.Send("\x1b[>q");
        Assert.Equal("\x1bP>|Centaur(0.1.0)\x1b\\", Assert.Single(responses));
    }
}
