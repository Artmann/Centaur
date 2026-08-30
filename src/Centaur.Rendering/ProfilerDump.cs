using System.Globalization;
using System.Text;

namespace Centaur.Rendering;

/// <summary>
/// Renders a <see cref="ProfilerSnapshot"/> as the multi-line console dump the profiler writes
/// every couple of seconds. Pure formatting over the same stage list the on-screen overlay uses,
/// so the two views can never drift apart.
/// </summary>
internal static class ProfilerDump
{
    public static string Format(ProfilerSnapshot s, string header)
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

        var stages = new ProfilerStage[ProfilerStages.Count];
        ProfilerStages.Fill(stages, s);
        foreach (var stage in stages)
        {
            sb.AppendLine(StageLine(c, stage));
        }

        sb.AppendLine(
            string.Format(
                c,
                "  {0,-14}{1,6:F3}ms ({2,3:F0}% of budget)",
                "total",
                s.TotalMs,
                ProfilerMath.Percent(s.TotalMs, s.FrameBudgetMs)
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

    static string StageLine(IFormatProvider c, ProfilerStage stage) =>
        stage.Share is { } share
            ? string.Format(c, "  {0,-14}{1,6:F3}ms ({2,3:F0}%)", stage.Label, stage.Ms, share)
            : string.Format(c, "  {0,-14}{1,6:F3}ms", stage.Label, stage.Ms);
}
