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
    // \x is a variable-length escape in C#, so the constant keeps the interpolated
    // sequences below unambiguous.
    const char esc = '\x1b';

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

    // A program that gets "not recognised" for the mouse modes turns mouse support off, even
    // though every one of these is implemented.
    [Theory]
    [InlineData(9)]
    [InlineData(1000)]
    [InlineData(1002)]
    [InlineData(1003)]
    [InlineData(1004)]
    [InlineData(1006)]
    [InlineData(1007)]
    public void Decrqm_MouseModes_AreRecognised(int mode)
    {
        parser.Send($"{esc}[?{mode}$p");
        Assert.DoesNotContain(";0$y", Assert.Single(responses));
    }

    [Fact]
    public void Decrqm_MouseTracking_RepliesSetOnceEnabled()
    {
        parser.Send("\x1b[?1003h");
        parser.Send("\x1b[?1003$p");
        Assert.Equal("\x1b[?1003;1$y", Assert.Single(responses));
    }

    // 1000/1002/1003 are one tracking level, so enabling any-event tracking must not leave
    // normal tracking claiming to be set as well.
    [Fact]
    public void Decrqm_LowerTrackingLevels_ReportResetWhenAHigherOneIsOn()
    {
        parser.Send("\x1b[?1003h");
        parser.Send("\x1b[?1000$p");
        Assert.Equal("\x1b[?1000;2$y", Assert.Single(responses));
    }

    // The extended encodings really are unimplemented, and saying so is what lets a program
    // fall back to a form this terminal does speak.
    [Fact]
    public void Decrqm_ExtendedMouseEncodings_StayUnrecognised()
    {
        parser.Send("\x1b[?1005$p");
        Assert.Equal("\x1b[?1005;0$y", Assert.Single(responses));
    }

    [Fact]
    public void Decrqm_UnknownMode_RepliesState0()
    {
        parser.Send("\x1b[?9999$p");
        Assert.Equal("\x1b[?9999;0$y", Assert.Single(responses));
    }
}
