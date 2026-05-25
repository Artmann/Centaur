using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Device attribute queries — DA1 (CSI c), DA2 (CSI &gt; c), DA3 (CSI = c).
///
/// Ported from ghostty/src/terminal/device_attributes.zig (tests
/// "primary default", "secondary default", "tertiary default"). TUIs gate
/// features on these replies, so the terminal must answer on the response
/// channel. The exact reply strings below mirror Ghostty's defaults and pin
/// the contract Centaur will implement.
///
/// Intended API (not yet implemented): VtParser raises the reply bytes on
/// its Respond channel when a DA query arrives.
/// </summary>
public class VtParserDeviceAttributesTests
{
    readonly VtParser parser;
    readonly List<string> responses;

    public VtParserDeviceAttributesTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
        responses = TerminalTestHelpers.CaptureResponses(parser);
    }

    [Fact]
    public void Da1_Primary_RepliesVt220WithAnsiColor()
    {
        // CSI c -> "ESC[?62;22c" (VT220 / level 2 conformance + ansi_color).
        parser.Send("\x1b[c");
        Assert.Equal("\x1b[?62;22c", Assert.Single(responses));
    }

    [Fact]
    public void Da1_ZeroParam_SameAsPrimary()
    {
        parser.Send("\x1b[0c");
        Assert.Equal("\x1b[?62;22c", Assert.Single(responses));
    }

    [Fact]
    public void Da2_Secondary_RepliesDeviceTypeAndVersion()
    {
        // CSI > c -> "ESC[>1;0;0c" (device type 1, firmware 0, rom 0).
        parser.Send("\x1b[>c");
        Assert.Equal("\x1b[>1;0;0c", Assert.Single(responses));
    }

    [Fact]
    public void Da3_Tertiary_RepliesUnitId()
    {
        // CSI = c -> DCS ! | 00000000 ST.
        parser.Send("\x1b[=c");
        Assert.Equal("\x1bP!|00000000\x1b\\", Assert.Single(responses));
    }
}
