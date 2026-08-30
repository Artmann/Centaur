using System.Diagnostics;

namespace Centaur.Rendering;

/// <summary>
/// Aggregates per-stage render timings into rolling 0.5s averages and surfaces them to the
/// on-screen <see cref="ProfilerOverlayExtension"/> and a periodic console dump.
///
/// Written from two threads: the render thread (<see cref="RecordFrame"/>) and the UI thread
/// (<see cref="RecordSnapshot"/>, the buffer-snapshot timing). All accumulator access is guarded
/// by a single lock; string formatting and the dump callback happen outside it.
///
/// A single instance is shared across all panes in a window, so the numbers are aggregated across
/// whichever panes rendered during the window — adequate for a developer profiler.
/// </summary>
public sealed class RenderProfiler
{
    readonly Func<long> timestampProvider;
    readonly double frequency;
    readonly Action<string> dumpWriter;
    readonly long windowTicks;
    readonly long dumpTicks;

    readonly object gate = new();

    Accumulators sums;
    long windowStartTimestamp;
    long lastDumpTimestamp;
    ProfilerSnapshot display;

    volatile bool enabled;

    public RenderProfiler(
        Func<long>? timestampProvider = null,
        double frequency = 0,
        Action<string>? dumpWriter = null
    )
    {
        this.timestampProvider = timestampProvider ?? Stopwatch.GetTimestamp;
        this.frequency = frequency > 0 ? frequency : Stopwatch.Frequency;
        this.dumpWriter = dumpWriter ?? Console.WriteLine;
        windowTicks = (long)(0.5 * this.frequency);
        dumpTicks = (long)(2.0 * this.frequency);
        FrameBudgetMs = 1000.0 / 75.0;

        var now = this.timestampProvider();
        windowStartTimestamp = now;
        lastDumpTimestamp = now;
    }

    /// <summary>Frame-time budget the overlay compares the total against. Defaults to ~13.3ms (75fps).</summary>
    public double FrameBudgetMs { get; set; }

    /// <summary>
    /// Off by default. Enabling resets the accumulators for a clean window; disabling writes a
    /// final summary through the dump writer and resets.
    /// </summary>
    public bool Enabled
    {
        get => enabled;
        set
        {
            if (value == enabled)
            {
                return;
            }

            string? finalDump = null;
            lock (gate)
            {
                var now = timestampProvider();
                if (value)
                {
                    ResetWindow(now);
                    display = default;
                    lastDumpTimestamp = now;
                    enabled = true;
                }
                else
                {
                    var snap = ComputeSnapshot(now);
                    ResetWindow(now);
                    display = default;
                    lastDumpTimestamp = now;
                    enabled = false;
                    finalDump = ProfilerDump.Format(snap, "final summary");
                }
            }

            if (finalDump != null)
            {
                dumpWriter(finalDump);
            }
        }
    }

    /// <summary>Render thread, once per frame.</summary>
    public void RecordFrame(FrameTimings frame)
    {
        ProfilerSnapshot? toDump = null;
        lock (gate)
        {
            sums.Add(frame);

            var now = timestampProvider();
            if (now - windowStartTimestamp >= windowTicks)
            {
                display = ComputeSnapshot(now);
                ResetWindow(now);
            }

            if (enabled && now - lastDumpTimestamp >= dumpTicks)
            {
                lastDumpTimestamp = now;
                toDump = display;
            }
        }

        if (toDump is { } d)
        {
            dumpWriter(ProfilerDump.Format(d, "frame avg over 0.5s, aggregated across panes"));
        }
    }

    /// <summary>UI thread, once per <c>Render(DrawingContext)</c>: the time spent under bufferLock.</summary>
    public void RecordSnapshot(long snapshotTicks)
    {
        lock (gate)
        {
            sums.SnapshotTicks += snapshotTicks;
        }
    }

    /// <summary>Render thread: cheap read of the latest already-averaged values for the overlay.</summary>
    public ProfilerSnapshot GetDisplaySnapshot()
    {
        lock (gate)
        {
            return display;
        }
    }

    // Caller must hold the lock.
    ProfilerSnapshot ComputeSnapshot(long now)
    {
        var frames = sums.FrameCount;
        var elapsedSeconds = (now - windowStartTimestamp) / frequency;
        var fps = frames > 0 && elapsedSeconds > 0 ? frames / elapsedSeconds : 0.0;

        return new ProfilerSnapshot(
            ClearMs: MeanMs(sums.ClearTicks, frames),
            BackgroundMs: MeanMs(sums.BackgroundTicks, frames),
            GlyphCollectMs: MeanMs(sums.GlyphCollectTicks, frames),
            GlyphDrawMs: MeanMs(sums.GlyphDrawTicks, frames),
            CursorMs: MeanMs(sums.CursorTicks, frames),
            OverlayMs: MeanMs(sums.OverlayTicks, frames),
            TotalMs: MeanMs(sums.TotalTicks, frames),
            SnapshotMs: MeanMs(sums.SnapshotTicks, frames),
            AllocKbPerFrame: ProfilerMath.BytesToKb(sums.AllocatedBytes, frames),
            Gen0PerWindow: sums.Gen0Collections,
            Fps: fps,
            FrameBudgetMs: FrameBudgetMs
        );
    }

    // Caller must hold the lock.
    void ResetWindow(long now)
    {
        sums = default;
        windowStartTimestamp = now;
    }

    double MeanMs(long sumTicks, int frames) =>
        ProfilerMath.Average(sumTicks, frames) / frequency * 1000.0;

    /// <summary>Running totals for the current window. Reset by assigning <c>default</c>.</summary>
    struct Accumulators
    {
        public int FrameCount;
        public long ClearTicks;
        public long BackgroundTicks;
        public long GlyphCollectTicks;
        public long GlyphDrawTicks;
        public long CursorTicks;
        public long OverlayTicks;
        public long TotalTicks;
        public long SnapshotTicks;
        public long AllocatedBytes;
        public int Gen0Collections;

        public void Add(FrameTimings frame)
        {
            FrameCount++;
            ClearTicks += frame.ClearTicks;
            BackgroundTicks += frame.BackgroundTicks;
            GlyphCollectTicks += frame.GlyphCollectTicks;
            GlyphDrawTicks += frame.GlyphDrawTicks;
            CursorTicks += frame.CursorTicks;
            OverlayTicks += frame.OverlayTicks;
            TotalTicks += frame.TotalTicks;
            AllocatedBytes += frame.AllocatedBytesDelta;
            Gen0Collections += frame.Gen0CollectionsDelta;
        }
    }
}

public readonly record struct ProfilerSnapshot(
    double ClearMs,
    double BackgroundMs,
    double GlyphCollectMs,
    double GlyphDrawMs,
    double CursorMs,
    double OverlayMs,
    double TotalMs,
    double SnapshotMs,
    double AllocKbPerFrame,
    int Gen0PerWindow,
    double Fps,
    double FrameBudgetMs
);
