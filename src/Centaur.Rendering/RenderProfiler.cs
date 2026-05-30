using System.Diagnostics;
using System.Globalization;
using System.Text;

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

    long sumClear,
        sumBackground,
        sumGlyphCollect,
        sumGlyphDraw,
        sumCursor,
        sumOverlay,
        sumTotal,
        sumSnapshot,
        sumAllocBytes;
    int sumGen0;
    int frameCount;
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
                    finalDump = FormatDump(snap, "final summary");
                }
            }

            if (finalDump != null)
            {
                dumpWriter(finalDump);
            }
        }
    }

    /// <summary>Render thread, once per frame. Arguments are raw <see cref="Stopwatch"/> tick deltas.</summary>
    public void RecordFrame(
        long clearTicks,
        long backgroundTicks,
        long glyphCollectTicks,
        long glyphDrawTicks,
        long cursorTicks,
        long overlayTicks,
        long totalTicks,
        long allocatedBytesDelta,
        int gen0CollectionsDelta
    )
    {
        ProfilerSnapshot? toDump = null;
        lock (gate)
        {
            frameCount++;
            sumClear += clearTicks;
            sumBackground += backgroundTicks;
            sumGlyphCollect += glyphCollectTicks;
            sumGlyphDraw += glyphDrawTicks;
            sumCursor += cursorTicks;
            sumOverlay += overlayTicks;
            sumTotal += totalTicks;
            sumAllocBytes += allocatedBytesDelta;
            sumGen0 += gen0CollectionsDelta;

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
            dumpWriter(FormatDump(d, "frame avg over 0.5s, aggregated across panes"));
        }
    }

    /// <summary>UI thread, once per <c>Render(DrawingContext)</c>: the time spent under bufferLock.</summary>
    public void RecordSnapshot(long snapshotTicks)
    {
        lock (gate)
        {
            sumSnapshot += snapshotTicks;
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
        var frames = frameCount;
        var elapsedSeconds = (now - windowStartTimestamp) / frequency;
        var fps = frames > 0 && elapsedSeconds > 0 ? frames / elapsedSeconds : 0.0;

        return new ProfilerSnapshot(
            ClearMs: MeanMs(sumClear, frames),
            BackgroundMs: MeanMs(sumBackground, frames),
            GlyphCollectMs: MeanMs(sumGlyphCollect, frames),
            GlyphDrawMs: MeanMs(sumGlyphDraw, frames),
            CursorMs: MeanMs(sumCursor, frames),
            OverlayMs: MeanMs(sumOverlay, frames),
            TotalMs: MeanMs(sumTotal, frames),
            SnapshotMs: MeanMs(sumSnapshot, frames),
            AllocKbPerFrame: BytesToKb(sumAllocBytes, frames),
            Gen0PerWindow: sumGen0,
            Fps: fps,
            FrameBudgetMs: FrameBudgetMs
        );
    }

    // Caller must hold the lock.
    void ResetWindow(long now)
    {
        sumClear = 0;
        sumBackground = 0;
        sumGlyphCollect = 0;
        sumGlyphDraw = 0;
        sumCursor = 0;
        sumOverlay = 0;
        sumTotal = 0;
        sumSnapshot = 0;
        sumAllocBytes = 0;
        sumGen0 = 0;
        frameCount = 0;
        windowStartTimestamp = now;
    }

    double MeanMs(long sumTicks, int frames) => Average(sumTicks, frames) / frequency * 1000.0;

    string FormatDump(ProfilerSnapshot s, string header)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.AppendLine(
            string.Format(
                c,
                "[render-profiler] {0} @ {1:F0}fps ({2:F1}ms budget)",
                header,
                s.Fps,
                s.FrameBudgetMs
            )
        );
        sb.AppendLine(string.Format(c, "  snapshot      {0,6:F3}ms", s.SnapshotMs));
        sb.AppendLine(
            string.Format(
                c,
                "  clear         {0,6:F3}ms ({1,3:F0}%)",
                s.ClearMs,
                Percent(s.ClearMs, s.TotalMs)
            )
        );
        sb.AppendLine(
            string.Format(
                c,
                "  background    {0,6:F3}ms ({1,3:F0}%)",
                s.BackgroundMs,
                Percent(s.BackgroundMs, s.TotalMs)
            )
        );
        sb.AppendLine(
            string.Format(
                c,
                "  glyphCollect  {0,6:F3}ms ({1,3:F0}%)",
                s.GlyphCollectMs,
                Percent(s.GlyphCollectMs, s.TotalMs)
            )
        );
        sb.AppendLine(
            string.Format(
                c,
                "  glyphDraw     {0,6:F3}ms ({1,3:F0}%)",
                s.GlyphDrawMs,
                Percent(s.GlyphDrawMs, s.TotalMs)
            )
        );
        sb.AppendLine(
            string.Format(
                c,
                "  cursor        {0,6:F3}ms ({1,3:F0}%)",
                s.CursorMs,
                Percent(s.CursorMs, s.TotalMs)
            )
        );
        sb.AppendLine(string.Format(c, "  overlays      {0,6:F3}ms", s.OverlayMs));
        sb.AppendLine(
            string.Format(
                c,
                "  total         {0,6:F3}ms ({1,3:F0}% of budget)",
                s.TotalMs,
                Percent(s.TotalMs, s.FrameBudgetMs)
            )
        );
        sb.Append(
            string.Format(
                c,
                "  alloc         {0:F1} KB/frame, gen0 +{1}",
                s.AllocKbPerFrame,
                s.Gen0PerWindow
            )
        );
        return sb.ToString();
    }

    internal static double TicksToMs(long ticks, double frequency) => ticks / frequency * 1000.0;

    internal static double Average(long sumTicks, int frames) =>
        frames == 0 ? 0.0 : (double)sumTicks / frames;

    internal static double Percent(double part, double whole) =>
        whole == 0.0 ? 0.0 : part / whole * 100.0;

    internal static double BytesToKb(long bytes, int frames) =>
        frames == 0 ? 0.0 : (double)bytes / 1024.0 / frames;
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
