using System.Diagnostics;

namespace Centaur.Rendering;

/// <summary>
/// Drives the per-vsync render decision: when nothing has changed, skip the redraw. A
/// `MarkDirty()` call from any visual-state mutation queues exactly one render on the next
/// tick; an optional 500ms heartbeat keeps self-updating overlays (the FPS counter and the
/// profiler panel) alive even on an otherwise idle terminal.
///
/// All state changes are quick enough that contention is irrelevant in practice; the
/// volatile flag keeps `MarkDirty()` lock-free for the PTY read thread.
/// </summary>
public sealed class FrameScheduler
{
    readonly long heartbeatTicks;
    long lastHeartbeatTimestamp;
    volatile bool dirty;

    public FrameScheduler(double frequency = 0)
    {
        var hz = frequency > 0 ? frequency : Stopwatch.Frequency;
        heartbeatTicks = (long)(0.5 * hz);
        dirty = true; // first frame after construction always renders
    }

    /// <summary>
    /// Marks the next tick as needing a render. Safe to call from any thread.
    /// </summary>
    public void MarkDirty() => dirty = true;

    /// <summary>
    /// Called once per vsync. When <paramref name="overlaysSelfUpdating"/> is true and the
    /// heartbeat interval has elapsed since the last render, this returns true to force a
    /// refresh of those overlays. Otherwise it returns true iff <see cref="MarkDirty"/> has
    /// been called since the last render.
    /// </summary>
    public bool Tick(long timestamp, bool overlaysSelfUpdating)
    {
        if (overlaysSelfUpdating && timestamp - lastHeartbeatTimestamp >= heartbeatTicks)
        {
            dirty = true;
            lastHeartbeatTimestamp = timestamp;
        }

        if (dirty)
        {
            dirty = false;
            return true;
        }
        return false;
    }
}
