using Centaur.App;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Where a pointer event goes: to the program as a report, to the pane as a selection or a
/// scrollback move, to the program as arrow keys, or nowhere.
///
/// <see cref="TerminalMouse.Route"/> is the whole decision and takes no Avalonia types, so
/// these are plain unit tests - the control's job is only to supply the event and act on the
/// answer.
/// </summary>
public class TerminalMouseTests
{
    static MouseOutcome Route(
        MouseButton button,
        MouseAction action,
        MouseTrackingMode tracking,
        bool shift = false,
        bool alternateScreen = false,
        bool altScroll = true
    ) =>
        TerminalMouse.Route(
            button,
            action,
            shift,
            new MouseModes(tracking, alternateScreen, altScroll)
        );

    // === Shift is the user's escape hatch ===

    [Fact]
    public void Shift_KeepsThePointerLocal_EvenUnderAnyEventTracking()
    {
        var outcome = Route(
            MouseButton.Left,
            MouseAction.Press,
            MouseTrackingMode.AnyEvent,
            shift: true
        );

        Assert.Equal(MouseOutcome.Local, outcome);
    }

    [Fact]
    public void Shift_KeepsTheWheelOnScrollback_OnTheAlternateScreen()
    {
        var outcome = Route(
            MouseButton.ScrollUp,
            MouseAction.Press,
            MouseTrackingMode.Off,
            shift: true,
            alternateScreen: true
        );

        Assert.Equal(MouseOutcome.Local, outcome);
    }

    // === Tracking off ===

    [Fact]
    public void NoTracking_LeavesTheClickToTheSelection()
    {
        Assert.Equal(
            MouseOutcome.Local,
            Route(MouseButton.Left, MouseAction.Press, MouseTrackingMode.Off)
        );
    }

    [Fact]
    public void NoTracking_MainScreen_LeavesTheWheelOnScrollback()
    {
        Assert.Equal(
            MouseOutcome.Local,
            Route(MouseButton.ScrollUp, MouseAction.Press, MouseTrackingMode.Off)
        );
    }

    // The reported defect: a full-screen program with no mouse tracking gets nothing from the
    // wheel today, because there is no scrollback on the alternate screen to move.
    [Fact]
    public void NoTracking_AlternateScreen_SendsTheWheelAsArrowKeys()
    {
        Assert.Equal(
            MouseOutcome.ScrollKeys,
            Route(
                MouseButton.ScrollUp,
                MouseAction.Press,
                MouseTrackingMode.Off,
                alternateScreen: true
            )
        );
    }

    [Fact]
    public void AlternateScrollTurnedOff_LeavesTheWheelAlone()
    {
        Assert.Equal(
            MouseOutcome.Local,
            Route(
                MouseButton.ScrollUp,
                MouseAction.Press,
                MouseTrackingMode.Off,
                alternateScreen: true,
                altScroll: false
            )
        );
    }

    // === Per-mode motion gating ===

    [Fact]
    public void Normal_ReportsPressAndRelease()
    {
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.Left, MouseAction.Press, MouseTrackingMode.Normal)
        );
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.Left, MouseAction.Release, MouseTrackingMode.Normal)
        );
    }

    // Motion is swallowed rather than made local: the program owns the pointer, and starting
    // a selection under it would draw a highlight the program knows nothing about.
    [Fact]
    public void Normal_SwallowsMotion()
    {
        Assert.Equal(
            MouseOutcome.Ignore,
            Route(MouseButton.Left, MouseAction.Motion, MouseTrackingMode.Normal)
        );
    }

    [Fact]
    public void ButtonEvent_ReportsMotionOnlyWhileAButtonIsDown()
    {
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.Left, MouseAction.Motion, MouseTrackingMode.ButtonEvent)
        );
        Assert.Equal(
            MouseOutcome.Ignore,
            Route(MouseButton.None, MouseAction.Motion, MouseTrackingMode.ButtonEvent)
        );
    }

    [Fact]
    public void AnyEvent_ReportsMotionWithNoButtonDown()
    {
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.None, MouseAction.Motion, MouseTrackingMode.AnyEvent)
        );
    }

    // === X10 (mode 9): presses only ===

    [Fact]
    public void X10_ReportsPressAndNothingElse()
    {
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.Left, MouseAction.Press, MouseTrackingMode.X10)
        );
        Assert.Equal(
            MouseOutcome.Ignore,
            Route(MouseButton.Left, MouseAction.Release, MouseTrackingMode.X10)
        );
        Assert.Equal(
            MouseOutcome.Ignore,
            Route(MouseButton.Left, MouseAction.Motion, MouseTrackingMode.X10)
        );
    }

    // === The wheel under tracking goes to the program, whichever screen is up ===

    [Fact]
    public void AnyTracking_ReportsTheWheel()
    {
        Assert.Equal(
            MouseOutcome.Report,
            Route(MouseButton.ScrollDown, MouseAction.Press, MouseTrackingMode.Normal)
        );
        Assert.Equal(
            MouseOutcome.Report,
            Route(
                MouseButton.ScrollUp,
                MouseAction.Press,
                MouseTrackingMode.ButtonEvent,
                alternateScreen: true
            )
        );
    }
}
