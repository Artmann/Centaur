using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Mouse reporting: SGR (1006) report encoding plus the DEC private modes
/// that enable/disable tracking.
///
/// Encoding ported from ghostty/src/terminal/c/mouse_encode.zig
/// ("encode: sgr press left") and the encoding rules in
/// ghostty/src/input/mouse_encode.zig. SGR format is
/// "ESC [ < {button} ; {col} ; {row} {M|m}" with 1-based col/row, where the
/// button code is left=0/middle=1/right=2/scrollUp=64/scrollDown=65, with
/// +4 shift / +8 alt / +16 ctrl, and +32 for motion; press/motion use 'M',
/// release uses 'm'.
///
/// Intended API (not yet implemented):
///   enum MouseButton { Left, Middle, Right, ScrollUp, ScrollDown, None }
///   enum MouseAction { Press, Release, Motion }
///   [Flags] enum MouseModifiers { None, Shift, Alt, Ctrl }
///   enum MouseTrackingMode { Off, X10, Normal, ButtonEvent, AnyEvent }
///   static class MouseReport { static string EncodeSgr(MouseButton, int col,
///       int row, MouseAction, MouseModifiers); }   // col/row are 0-based
///   VtParser: MouseTrackingMode MouseTracking; bool MouseSgrMode;
///       bool FocusEventMode; bool AltScrollMode;
/// </summary>
public class VtParserMouseTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserMouseTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    // === SGR report encoding ===

    [Fact]
    public void Sgr_PressLeft_AtOrigin()
    {
        // Ported from "encode: sgr press left": (0,0) press left -> ESC[<0;1;1M
        var report = MouseReport.EncodeSgr(
            MouseButton.Left,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );
        Assert.Equal("\x1b[<0;1;1M", report);
    }

    [Fact]
    public void Sgr_ReleaseLeft_UsesLowercaseM()
    {
        var report = MouseReport.EncodeSgr(
            MouseButton.Left,
            0,
            0,
            MouseAction.Release,
            MouseModifiers.None
        );
        Assert.Equal("\x1b[<0;1;1m", report);
    }

    [Fact]
    public void Sgr_RightButton_Code2()
    {
        var report = MouseReport.EncodeSgr(
            MouseButton.Right,
            9,
            4,
            MouseAction.Press,
            MouseModifiers.None
        );
        Assert.Equal("\x1b[<2;10;5M", report);
    }

    [Fact]
    public void Sgr_ScrollUp_Code64()
    {
        var report = MouseReport.EncodeSgr(
            MouseButton.ScrollUp,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );
        Assert.Equal("\x1b[<64;1;1M", report);
    }

    [Fact]
    public void Sgr_Motion_AddsThirtyTwo()
    {
        // Motion with left held: 0 + 32 = 32.
        var report = MouseReport.EncodeSgr(
            MouseButton.Left,
            5,
            6,
            MouseAction.Motion,
            MouseModifiers.None
        );
        Assert.Equal("\x1b[<32;6;7M", report);
    }

    [Fact]
    public void Sgr_Modifiers_AddBits()
    {
        // left(0) + shift(4) + ctrl(16) = 20.
        var report = MouseReport.EncodeSgr(
            MouseButton.Left,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.Shift | MouseModifiers.Ctrl
        );
        Assert.Equal("\x1b[<20;1;1M", report);
    }

    // === DEC private mode enable/disable ===

    [Fact]
    public void MouseTracking_DefaultOff()
    {
        Assert.Equal(MouseTrackingMode.Off, parser.MouseTracking);
    }

    [Fact]
    public void MouseTracking_Normal_Mode1000()
    {
        parser.Send("\x1b[?1000h");
        Assert.Equal(MouseTrackingMode.Normal, parser.MouseTracking);
        parser.Send("\x1b[?1000l");
        Assert.Equal(MouseTrackingMode.Off, parser.MouseTracking);
    }

    [Fact]
    public void MouseTracking_ButtonEvent_Mode1002()
    {
        parser.Send("\x1b[?1002h");
        Assert.Equal(MouseTrackingMode.ButtonEvent, parser.MouseTracking);
    }

    [Fact]
    public void MouseTracking_AnyEvent_Mode1003()
    {
        parser.Send("\x1b[?1003h");
        Assert.Equal(MouseTrackingMode.AnyEvent, parser.MouseTracking);
    }

    [Fact]
    public void MouseSgrMode_Mode1006()
    {
        Assert.False(parser.MouseSgrMode);
        parser.Send("\x1b[?1006h");
        Assert.True(parser.MouseSgrMode);
        parser.Send("\x1b[?1006l");
        Assert.False(parser.MouseSgrMode);
    }

    [Fact]
    public void FocusEventMode_Mode1004()
    {
        parser.Send("\x1b[?1004h");
        Assert.True(parser.FocusEventMode);
    }

    [Fact]
    public void AltScrollMode_Mode1007()
    {
        parser.Send("\x1b[?1007h");
        Assert.True(parser.AltScrollMode);
    }
}
