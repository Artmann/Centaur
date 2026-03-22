namespace Centaur.Core.Terminal;

public record FuzzyMatchResult(int Score, IReadOnlyList<int> MatchedIndices);

public static class FuzzyMatcher
{
    public static FuzzyMatchResult? Match(string pattern, string candidate)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(candidate))
        {
            return null;
        }

        var matchedIndices = new List<int>();
        var patternIndex = 0;

        for (int i = 0; i < candidate.Length && patternIndex < pattern.Length; i++)
        {
            if (char.ToLowerInvariant(candidate[i]) == char.ToLowerInvariant(pattern[patternIndex]))
            {
                matchedIndices.Add(i);
                patternIndex++;
            }
        }

        if (patternIndex < pattern.Length)
        {
            return null;
        }

        var score = ComputeScore(matchedIndices, candidate);
        return new FuzzyMatchResult(score, matchedIndices);
    }

    static int ComputeScore(List<int> matchedIndices, string candidate)
    {
        var score = 0;

        for (int i = 0; i < matchedIndices.Count; i++)
        {
            var index = matchedIndices[i];

            // Base score per match
            score += 1;

            // Consecutive match bonus
            if (i > 0 && index == matchedIndices[i - 1] + 1)
            {
                score += 5;
            }

            // Word boundary bonus
            if (index == 0 || IsWordBoundary(candidate[index - 1]))
            {
                score += 10;
            }

            // Start of string bonus
            if (index == 0)
            {
                score += 3;
            }
        }

        return score;
    }

    static bool IsWordBoundary(char c)
    {
        return c is ' ' or '-' or '_' or '/' or '.' or '\\';
    }
}
