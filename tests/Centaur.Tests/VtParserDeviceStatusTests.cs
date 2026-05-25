using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Device Status Report (DSR) queries — CSI 5 n (status) and CSI 6 n (cursor
/// position report / CPR). TUIs such as Claude Code's Ink renderer emit CSI 6 n
/// at startup and block waiting for the CSI row;col R reply; an unanswered query
/// stalls the program. Replies are 1-based, mirroring xterm/Ghostty.
/// </summary>
public class VtParserDeviceStatusTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly List<string> responses;

    public VtParserDeviceStatusTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
        responses = TerminalTestHelpers.CaptureResponses(parser);
    }

    [Fact]
    public void Dsr5_ReportsTerminalOk()
    {
        // CSI 5 n -> "ESC[0n" (terminal is functioning correctly).
        parser.Send("\x1b[5n");
        Assert.Equal("\x1b[0n", Assert.Single(responses));
    }

    [Fact]
    public void Dsr6_ReportsCursorPosition()
    {
        // Move to row 4, col 6 (1-based), then request CPR.
        parser.Send("\x1b[4;6H");
        parser.Send("\x1b[6n");
        Assert.Equal("\x1b[4;6R", Assert.Single(responses));
    }

    [Fact]
    public void Dsr6_DefaultPosition_ReportsOrigin()
    {
        // At the home position CPR is 1-based origin.
        parser.Send("\x1b[6n");
        Assert.Equal("\x1b[1;1R", Assert.Single(responses));
    }
}
