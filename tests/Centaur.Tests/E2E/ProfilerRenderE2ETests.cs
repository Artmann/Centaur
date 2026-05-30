using System.Diagnostics;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Drives the real render path (<see cref="TerminalRenderer.Render"/> via <see cref="RenderProbe"/>)
/// with a profiler attached, to verify the instrumentation records frames and does not change the
/// rendered output. A controllable clock (running at the real Stopwatch frequency) forces the 0.5s
/// window boundary without sleeping, so the test stays fast and deterministic.
/// </summary>
public class ProfilerRenderE2ETests
{
    static readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    static ScreenBuffer BufferWithText(string text)
    {
        var buffer = new ScreenBuffer(80, 24, theme);
        var parser = new VtParser(buffer, theme);
        parser.Send(text);
        return buffer.Snapshot();
    }

    [Fact]
    public void EnabledProfiler_RecordsFramesAcrossWindow()
    {
        var frequency = Stopwatch.Frequency;
        var clock = new long[] { 0 };
        var profiler = new RenderProfiler(() => clock[0], frequency, _ => { });
        profiler.Enabled = true;

        using var renderer = new TerminalRenderer(theme, profiler: profiler);
        var snapshot = BufferWithText("hello profiler");

        // Frame 1 inside the window, then advance one full second and render frame 2 to cross
        // the 0.5s boundary so the rolling averages are computed.
        RenderProbe.RenderToBitmap(snapshot, renderer).Dispose();
        clock[0] = frequency;
        using var bitmap = RenderProbe.RenderToBitmap(snapshot, renderer);

        var s = profiler.GetDisplaySnapshot();
        // Two frames over one second of fake-clock time -> deterministic 2fps; proves RecordFrame ran.
        Assert.Equal(2.0, s.Fps, precision: 3);
        Assert.True(s.TotalMs >= 0.0);

        // Rendering still produced ink for the text (profiling must not change output).
        var background = RenderProbe.ToColor(theme.Background);
        var ink = RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, background);
        Assert.True(ink > 0, "expected glyph pixels for the first character");
    }

    [Fact]
    public void DisabledProfiler_RendersWithoutRecording()
    {
        var clock = new long[] { 0 };
        var profiler = new RenderProfiler(() => clock[0], Stopwatch.Frequency, _ => { });
        // Left disabled.

        using var renderer = new TerminalRenderer(theme, profiler: profiler);
        var snapshot = BufferWithText("hello");

        using var bitmap = RenderProbe.RenderToBitmap(snapshot, renderer);

        // No frames recorded while disabled.
        Assert.Equal(0.0, profiler.GetDisplaySnapshot().Fps);

        var background = RenderProbe.ToColor(theme.Background);
        Assert.True(RenderProbe.ForegroundPixelCount(bitmap, renderer, 0, 0, background) > 0);
    }
}
