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

    public PaneFrameLoop(Control owner, Func<bool> overlaysSelfUpdating)
    {
        this.owner = owner;
        this.overlaysSelfUpdating = overlaysSelfUpdating;
    }

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

        if (scheduler.Tick(Stopwatch.GetTimestamp(), overlaysSelfUpdating()))
        {
            owner.InvalidateVisual();
        }

        Schedule();
    }
}
