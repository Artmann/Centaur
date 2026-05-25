using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// DECRQM — request mode (CSI ? Ps $ p) and its DECRPM reply (CSI ? Ps ; Pm $ y).
///
/// Ported from ghostty/src/terminal/modes.zig (tests "getReport known DEC
/// mode", "getReport unknown mode", "Report.encode DEC mode set/reset",
/// "Report.encode not recognized"). The reply state is 0=not_recognized,
/// 1=set, 2=reset.
///
/// Intended API (not yet implemented): VtParser answers a DECRQM query on its
/// Respond channel, reflecting current mode state.
/// </summary>
public class VtParserDecrqmTests
{
    readonly VtParser parser;
    readonly List<string> responses;

    public VtParserDecrqmTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
        responses = TerminalTestHelpers.CaptureResponses(parser);
    }

    [Fact]
    public void Decrqm_ResetMode_RepliesState2()
    {
        // Bracketed paste (2004) defaults to off -> state 2 (reset).
        parser.Send("\x1b[?2004$p");
        Assert.Equal("\x1b[?2004;2$y", Assert.Single(responses));
    }

    [Fact]
    public void Decrqm_SetMode_RepliesState1()
    {
        parser.Send("\x1b[?2004h"); // enable bracketed paste
        parser.Send("\x1b[?2004$p");
        Assert.Equal("\x1b[?2004;1$y", Assert.Single(responses));
    }

    [Fact]
    public void Decrqm_UnknownMode_RepliesState0()
    {
        parser.Send("\x1b[?9999$p");
        Assert.Equal("\x1b[?9999;0$y", Assert.Single(responses));
    }
}
