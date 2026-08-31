using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The settings page's search. Ranks descriptors against a query with the same
/// <see cref="FuzzyMatcher"/> the reverse-search overlay uses, so typing behaves the same way in
/// both places.
/// </summary>
static class SettingsSearch
{
    /// <summary>
    /// Everything matching <paramref name="query"/>, best first. An empty query returns the whole
    /// list in registry order, which is what an unfiltered tab renders.
    /// </summary>
    public static IReadOnlyList<SettingDescriptor> Filter(
        IEnumerable<SettingDescriptor> settings,
        string query
    )
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0)
        {
            return settings.ToArray();
        }

        return settings
            .Select(setting => (setting, score: Score(setting, trimmed)))
            .Where(match => match.score > 0)
            .OrderByDescending(match => match.score)
            .ThenBy(match => match.setting.Title, StringComparer.CurrentCultureIgnoreCase)
            .Select(match => match.setting)
            .ToArray();
    }

    /// <summary>The best score any of a setting's searchable text achieves, or 0 for no match.</summary>
    static int Score(SettingDescriptor setting, string query)
    {
        // The title is what the user is most likely aiming at, so a title hit outranks the same
        // hit anywhere else.
        var best = Best(setting.Title, weight: 3);
        best = Math.Max(best, Best(setting.Section, weight: 2));

        foreach (var keyword in setting.Keywords)
        {
            best = Math.Max(best, Best(keyword, weight: 2));
        }

        // The description is matched by substring rather than fuzzily: it is a sentence, and a
        // subsequence match against a sentence hits nearly every query.
        if (setting.Description.Contains(query, StringComparison.CurrentCultureIgnoreCase))
        {
            best = Math.Max(best, 1);
        }

        return best;

        int Best(string candidate, int weight)
        {
            var match = FuzzyMatcher.Match(query, candidate);
            return match == null ? 0 : match.Score * weight;
        }
    }
}
