using System.Diagnostics;
using Avalonia.Controls;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// Drives a control's redraws off the compositor's vsync: the pane marks itself dirty from
/// wherever the change happened - including the PTY read thread - and at most one repaint
/// follows per frame.
///
/// The loop is only alive while the control is in the visual tree. Detaching pauses it rather
/// than tearing anything down, so a pane survives being re-parented into a split.
/// </summary>
public sealed class PaneFrameLoop
{
    readonly Control owner;
    readonly FrameScheduler scheduler = new();

    // Overlays that animate on their own clock (FPS counter, profiler) need frames even when
    // nothing in the terminal changed.
    readonly Func<bool> overlaysSelfUpdating;

    // Blinking cells are the one thing that needs a repaint on wall-clock time rather than on
    // terminal output, so the loop owns the phase and asks whether anything is blinking.
    readonly Func<bool> blinkingCellsPresent;

    public PaneFrameLoop(
        Control owner,
        Func<bool> overlaysSelfUpdating,
        Func<bool> blinkingCellsPresent
    )
    {
        this.owner = owner;
        this.overlaysSelfUpdating = overlaysSelfUpdating;
        this.blinkingCellsPresent = blinkingCellsPresent;
    }

    /// <summary>The SGR 5/6 blink half-cycle the next frame should be drawn in.</summary>
    public BlinkPhase Blink { get; } = new();

    /// <summary>True while the loop is running, i.e. the pane is on screen.</summary>
    public bool Running { get; private set; }

    public void Start()
    {
        Running = true;
        MarkDirty();
        Schedule();
    }

    public void Stop()
    {
        Running = false;
    }

    /// <summary>"Something visible changed; repaint on the next vsync." Safe from any thread.</summary>
    public void MarkDirty()
    {
        scheduler.MarkDirty();
    }

    void Schedule()
    {
        if (!Running)
        {
            return;
        }

        TopLevel.GetTopLevel(owner)?.RequestAnimationFrame(OnFrame);
    }

    void OnFrame(TimeSpan timestamp)
    {
        if (!Running)
        {
            return;
        }

        // Advance the blink phase, but only force a repaint when the last frame actually
        // contained blinking cells - an ordinary terminal must still idle at zero frames.
        if (Blink.Advance(timestamp) && blinkingCellsPresent())
        {
            MarkDirty();
        }

        if (scheduler.Tick(Stopwatch.GetTimestamp(), overlaysSelfUpdating()))
        {
            owner.InvalidateVisual();
        }

        Schedule();
    }
}
