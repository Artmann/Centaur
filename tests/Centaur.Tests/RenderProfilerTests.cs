using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

public class RenderProfilerTests
{
    // A controllable clock running at 1000 ticks/second, so 1 tick == 1 millisecond.
    // This makes window (0.5s = 500 ticks) and dump (2s = 2000 ticks) boundaries easy to drive.
    const double frequency = 1000.0;

    static RenderProfiler NewProfiler(long[] clock, Action<string>? dumpWriter = null) =>
        new(() => clock[0], frequency, dumpWriter ?? (_ => { }));

    [Fact]
    public void TicksToMs_ConvertsUsingFrequency()
    {
        Assert.Equal(1000.0, RenderProfiler.TicksToMs(1000, 1000.0));
        Assert.Equal(500.0, RenderProfiler.TicksToMs(500, 1000.0));
        Assert.Equal(0.0, RenderProfiler.TicksToMs(0, 1000.0));
    }

    [Fact]
    public void Average_ZeroFrames_ReturnsZero()
    {
        Assert.Equal(0.0, RenderProfiler.Average(300, 0));
    }

    [Fact]
    public void Average_ComputesMeanTicks()
    {
        Assert.Equal(100.0, RenderProfiler.Average(300, 3));
    }

    [Fact]
    public void Percent_ZeroWhole_ReturnsZero()
    {
        Assert.Equal(0.0, RenderProfiler.Percent(25, 0));
    }

    [Fact]
    public void Percent_ComputesRatio()
    {
        Assert.Equal(25.0, RenderProfiler.Percent(25, 100));
    }

    [Fact]
    public void BytesToKb_ZeroFrames_ReturnsZero()
    {
        Assert.Equal(0.0, RenderProfiler.BytesToKb(2048, 0));
    }

    [Fact]
    public void BytesToKb_DividesByFrames()
    {
        Assert.Equal(1.0, RenderProfiler.BytesToKb(2048, 2));
    }

    [Fact]
    public void Enabled_DefaultsToFalse()
    {
        var profiler = NewProfiler([0]);
        Assert.False(profiler.Enabled);
    }

    [Fact]
    public void GetDisplaySnapshot_BeforeAnyFrame_ReturnsZeros()
    {
        var profiler = NewProfiler([0]);

        var s = profiler.GetDisplaySnapshot();

        Assert.Equal(0.0, s.ClearMs);
        Assert.Equal(0.0, s.TotalMs);
        Assert.Equal(0.0, s.SnapshotMs);
        Assert.Equal(0.0, s.AllocKbPerFrame);
        Assert.Equal(0, s.Gen0PerWindow);
        Assert.Equal(0.0, s.Fps);
    }

    [Fact]
    public void RecordFrame_WithinWindow_DisplayUnchanged()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        // Two frames 100 ticks apart — still inside the 500-tick window.
        RecordOneFrame(profiler, totalTicks: 10);
        clock[0] = 100;
        RecordOneFrame(profiler, totalTicks: 10);

        // No window boundary crossed yet, so the display is still the initial zeros.
        Assert.Equal(0.0, profiler.GetDisplaySnapshot().TotalMs);
    }

    [Fact]
    public void WindowBoundary_ComputesAveragesAndResets()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        // Three frames totalling 30 ticks of "total" work inside the window.
        RecordOneFrame(profiler, totalTicks: 10);
        RecordOneFrame(profiler, totalTicks: 20);
        RecordOneFrame(profiler, totalTicks: 30);

        // Cross the 500-tick window boundary on the next frame.
        clock[0] = 600;
        RecordOneFrame(profiler, totalTicks: 99);

        var s = profiler.GetDisplaySnapshot();
        // Average of the four totals (10+20+30+99)/4 = 39.75 ticks -> ms at 1000Hz.
        Assert.Equal(39.75, s.TotalMs, precision: 5);

        // After the boundary the window resets: another sub-window frame must not
        // immediately recompute (display stays at the just-computed value).
        clock[0] = 650;
        RecordOneFrame(profiler, totalTicks: 5);
        Assert.Equal(39.75, profiler.GetDisplaySnapshot().TotalMs, precision: 5);
    }

    [Fact]
    public void TotalMs_OverBudget_Detectable()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);
        profiler.FrameBudgetMs = 13.3;

        RecordOneFrame(profiler, totalTicks: 50); // 50 ticks == 50ms at 1000Hz
        clock[0] = 600;
        RecordOneFrame(profiler, totalTicks: 50);

        var s = profiler.GetDisplaySnapshot();
        Assert.True(s.TotalMs > s.FrameBudgetMs);
    }

    [Fact]
    public void AllocKbPerFrame_AveragesOverFrames()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        RecordOneFrame(profiler, totalTicks: 1, allocBytes: 1024);
        RecordOneFrame(profiler, totalTicks: 1, allocBytes: 3072);
        clock[0] = 600;
        RecordOneFrame(profiler, totalTicks: 1, allocBytes: 0);

        // (1024 + 3072 + 0) / 1024 / 3 frames = 1.333... KB/frame
        Assert.Equal(
            4096.0 / 1024.0 / 3.0,
            profiler.GetDisplaySnapshot().AllocKbPerFrame,
            precision: 5
        );
    }

    [Fact]
    public void Gen0Delta_AccumulatesOverWindow()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        RecordOneFrame(profiler, totalTicks: 1, gen0: 1);
        RecordOneFrame(profiler, totalTicks: 1, gen0: 2);
        clock[0] = 600;
        RecordOneFrame(profiler, totalTicks: 1, gen0: 0);

        Assert.Equal(3, profiler.GetDisplaySnapshot().Gen0PerWindow);
    }

    [Fact]
    public void RecordSnapshot_ContributesToSnapshotMs()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        profiler.RecordSnapshot(4);
        RecordOneFrame(profiler, totalTicks: 1);
        profiler.RecordSnapshot(6);
        RecordOneFrame(profiler, totalTicks: 1);

        clock[0] = 600;
        profiler.RecordSnapshot(0);
        RecordOneFrame(profiler, totalTicks: 1);

        // (4 + 6 + 0) / 3 frames = 3.333... ms at 1000Hz
        Assert.Equal(10.0 / 3.0, profiler.GetDisplaySnapshot().SnapshotMs, precision: 5);
    }

    [Fact]
    public void Disable_TriggersFinalSummaryAndResets()
    {
        var clock = new long[] { 0 };
        var dumps = new List<string>();
        var profiler = NewProfiler(clock, dumps.Add);

        profiler.Enabled = true;
        RecordOneFrame(profiler, totalTicks: 42);
        profiler.Enabled = false;

        Assert.NotEmpty(dumps);

        // After disabling, accumulators are cleared: a fresh window yields zeros.
        clock[0] = 10_000;
        profiler.GetDisplaySnapshot(); // no throw
        Assert.Equal(0, profiler.GetDisplaySnapshot().Gen0PerWindow);
    }

    [Fact]
    public void ConcurrentRecord_NoTornState()
    {
        var clock = new long[] { 0 };
        var profiler = NewProfiler(clock);

        Parallel.For(
            0,
            1000,
            _ =>
            {
                profiler.RecordSnapshot(1);
                RecordOneFrame(profiler, totalTicks: 1);
            }
        );

        // Just assert it didn't throw and a read is well-formed.
        var s = profiler.GetDisplaySnapshot();
        Assert.True(s.TotalMs >= 0.0);
    }

    static void RecordOneFrame(
        RenderProfiler profiler,
        long totalTicks,
        long allocBytes = 0,
        int gen0 = 0
    )
    {
        profiler.RecordFrame(
            clearTicks: 0,
            backgroundTicks: 0,
            glyphCollectTicks: 0,
            glyphDrawTicks: 0,
            cursorTicks: 0,
            overlayTicks: 0,
            totalTicks: totalTicks,
            allocatedBytesDelta: allocBytes,
            gen0CollectionsDelta: gen0
        );
    }
}
