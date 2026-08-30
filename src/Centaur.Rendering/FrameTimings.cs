namespace Centaur.Rendering;

/// <summary>
/// One frame's raw measurements, in <see cref="System.Diagnostics.Stopwatch"/> ticks. Grouped
/// so the renderer hands the profiler a single value instead of nine positional longs that are
/// easy to transpose.
/// </summary>
public readonly record struct FrameTimings(
    long ClearTicks,
    long BackgroundTicks,
    long GlyphCollectTicks,
    long GlyphDrawTicks,
    long CursorTicks,
    long OverlayTicks,
    long TotalTicks,
    long AllocatedBytesDelta,
    int Gen0CollectionsDelta
);
