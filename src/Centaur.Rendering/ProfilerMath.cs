namespace Centaur.Rendering;

/// <summary>
/// The unit conversions the profiler reports in. Kept apart from <see cref="RenderProfiler"/>
/// so the accumulator and the arithmetic can be read — and tested — independently.
/// </summary>
internal static class ProfilerMath
{
    public static double TicksToMs(long ticks, double frequency) => ticks / frequency * 1000.0;

    /// <summary>Mean ticks per frame, or 0 for a window that saw no frames.</summary>
    public static double Average(long sumTicks, int frames) =>
        frames == 0 ? 0.0 : (double)sumTicks / frames;

    /// <summary><paramref name="part"/> as a percentage of <paramref name="whole"/>, guarding
    /// against the zero-frame window where every total is still 0.</summary>
    public static double Percent(double part, double whole) =>
        whole == 0.0 ? 0.0 : part / whole * 100.0;

    public static double BytesToKb(long bytes, int frames) =>
        frames == 0 ? 0.0 : (double)bytes / 1024.0 / frames;
}
