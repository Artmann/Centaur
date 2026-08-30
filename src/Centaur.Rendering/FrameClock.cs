using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Centaur.Rendering;

/// <summary>
/// Stopwatch marks for one render pass, taken in the order <see cref="TerminalRenderer.Render"/>
/// lays its passes out: clear, background, glyph collect, glyph draw, cursor, overlays. Takes no
/// timestamps at all when profiling is off, so an unprofiled frame pays nothing for the calls.
/// </summary>
internal struct FrameClock
{
    const int markCount = 6;

    [InlineArray(markCount)]
    struct Marks
    {
        long first;
    }

    readonly bool active;
    readonly long start;
    readonly long allocBefore;
    readonly int gen0Before;

    Marks marks;
    int next;

    FrameClock(bool active)
    {
        this.active = active;
        if (!active)
        {
            return;
        }

        allocBefore = GC.GetAllocatedBytesForCurrentThread();
        gen0Before = GC.CollectionCount(0);
        start = Stopwatch.GetTimestamp();
    }

    public static FrameClock Start(bool active) => new(active);

    /// <summary>Closes the pass that just ran.</summary>
    public void Mark()
    {
        if (!active)
        {
            return;
        }

        marks[next++] = Stopwatch.GetTimestamp();
    }

    /// <summary>The frame's timings, or false when profiling was off for this frame. Total spans
    /// the whole frame, overlays included, so the per-stage shares add up.</summary>
    public bool TryFinish(out FrameTimings timings)
    {
        if (!active)
        {
            timings = default;
            return false;
        }

        timings = new FrameTimings(
            ClearTicks: marks[0] - start,
            BackgroundTicks: marks[1] - marks[0],
            GlyphCollectTicks: marks[2] - marks[1],
            GlyphDrawTicks: marks[3] - marks[2],
            CursorTicks: marks[4] - marks[3],
            OverlayTicks: marks[5] - marks[4],
            TotalTicks: marks[5] - start,
            AllocatedBytesDelta: GC.GetAllocatedBytesForCurrentThread() - allocBefore,
            Gen0CollectionsDelta: GC.CollectionCount(0) - gen0Before
        );
        return true;
    }
}
