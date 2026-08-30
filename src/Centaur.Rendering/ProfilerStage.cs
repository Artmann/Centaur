namespace Centaur.Rendering;

/// <summary>
/// One render stage as the profiler reports it. <see cref="Share"/> is the stage's percentage
/// of the frame total, or null for the buffer snapshot, which happens on the UI thread before
/// the frame starts and so is not part of it.
/// </summary>
public readonly record struct ProfilerStage(string Label, double Ms, double? Share);

/// <summary>
/// The stage breakdown, in display order. Shared so the console dump and the on-screen overlay
/// cannot drift apart on which stages exist or what they are called.
/// </summary>
public static class ProfilerStages
{
    public const int Count = 7;

    /// <summary>Fills <paramref name="into"/> (length <see cref="Count"/>) with the breakdown.
    /// Takes a span so the overlay can reuse one buffer instead of allocating per frame.</summary>
    public static void Fill(Span<ProfilerStage> into, ProfilerSnapshot s)
    {
        into[0] = new ProfilerStage("snapshot", s.SnapshotMs, null);
        into[1] = Stage("clear", s.ClearMs, s.TotalMs);
        into[2] = Stage("background", s.BackgroundMs, s.TotalMs);
        into[3] = Stage("glyphCollect", s.GlyphCollectMs, s.TotalMs);
        into[4] = Stage("glyphDraw", s.GlyphDrawMs, s.TotalMs);
        into[5] = Stage("cursor", s.CursorMs, s.TotalMs);
        into[6] = Stage("overlays", s.OverlayMs, s.TotalMs);
    }

    static ProfilerStage Stage(string label, double ms, double totalMs) =>
        new(label, ms, ProfilerMath.Percent(ms, totalMs));
}
