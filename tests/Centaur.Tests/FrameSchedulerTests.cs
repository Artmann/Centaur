using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

public class FrameSchedulerTests
{
    // A controllable clock running at 1000 ticks/second, so 1 tick == 1 millisecond.
    // The heartbeat is hardcoded to 0.5s, i.e. 500 ticks at this frequency.
    const double frequency = 1000.0;

    [Fact]
    public void FirstTick_AlwaysRenders()
    {
        var scheduler = new FrameScheduler(frequency);

        Assert.True(scheduler.Tick(timestamp: 0, overlaysSelfUpdating: false));
    }

    [Fact]
    public void SecondTickWithoutSignal_Skips()
    {
        var scheduler = new FrameScheduler(frequency);

        scheduler.Tick(timestamp: 0, overlaysSelfUpdating: false);

        Assert.False(scheduler.Tick(timestamp: 100, overlaysSelfUpdating: false));
    }

    [Fact]
    public void MarkDirty_CausesNextTickToRender_ThenSkipsAgain()
    {
        var scheduler = new FrameScheduler(frequency);

        scheduler.Tick(timestamp: 0, overlaysSelfUpdating: false);
        scheduler.MarkDirty();

        Assert.True(scheduler.Tick(timestamp: 50, overlaysSelfUpdating: false));
        Assert.False(scheduler.Tick(timestamp: 100, overlaysSelfUpdating: false));
    }

    [Fact]
    public void Heartbeat_RendersEvery500MsWhenOverlaysSelfUpdate()
    {
        var scheduler = new FrameScheduler(frequency);

        // Initial render at t=0 clears the dirty flag and seeds the heartbeat baseline.
        Assert.True(scheduler.Tick(timestamp: 0, overlaysSelfUpdating: true));

        // 100ms later — no signal, heartbeat hasn't elapsed yet, must skip.
        Assert.False(scheduler.Tick(timestamp: 100, overlaysSelfUpdating: true));

        // 500ms boundary — heartbeat fires.
        Assert.True(scheduler.Tick(timestamp: 500, overlaysSelfUpdating: true));

        // Right after the heartbeat — back to skipping until the next 500ms boundary.
        Assert.False(scheduler.Tick(timestamp: 600, overlaysSelfUpdating: true));
        Assert.True(scheduler.Tick(timestamp: 1000, overlaysSelfUpdating: true));
    }

    [Fact]
    public void NoHeartbeat_WhenOverlaysQuiet_OnlyInitialRender()
    {
        var scheduler = new FrameScheduler(frequency);

        Assert.True(scheduler.Tick(timestamp: 0, overlaysSelfUpdating: false));

        // 75 fps for a full second — all should skip without any dirty signal.
        for (long t = 13; t <= 1000; t += 13)
        {
            Assert.False(scheduler.Tick(timestamp: t, overlaysSelfUpdating: false));
        }
    }

    [Fact]
    public void HeartbeatDoesNotFire_WhenOverlaysToggledOff()
    {
        var scheduler = new FrameScheduler(frequency);

        // Seed with overlays on at t=0.
        scheduler.Tick(timestamp: 0, overlaysSelfUpdating: true);

        // Overlays turn off; even past the 500ms boundary, no heartbeat tick.
        Assert.False(scheduler.Tick(timestamp: 500, overlaysSelfUpdating: false));
        Assert.False(scheduler.Tick(timestamp: 1500, overlaysSelfUpdating: false));
    }

    [Fact]
    public void MarkDirty_TakesPrecedenceOverHeartbeat()
    {
        var scheduler = new FrameScheduler(frequency);
        scheduler.Tick(timestamp: 0, overlaysSelfUpdating: true);

        scheduler.MarkDirty();
        // Even though heartbeat hasn't elapsed, MarkDirty forces a render.
        Assert.True(scheduler.Tick(timestamp: 50, overlaysSelfUpdating: true));
    }
}
