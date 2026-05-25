using System.Text.RegularExpressions;

namespace Centaur.Core.Terminal;

/// <summary>
/// Finds http(s) URLs within a single rendered line so the UI can make them
/// clickable. Trailing sentence punctuation is excluded from the match.
/// </summary>
public static partial class UrlDetector
{
    // Match an http/https scheme followed by any run of non-whitespace.
    [GeneratedRegex(@"https?://\S+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // Punctuation that commonly follows a URL in prose but is not part of it.
    static readonly char[] trailingPunctuation =
    {
        '.',
        ',',
        ';',
        ':',
        '!',
        '?',
        ')',
        ']',
        '}',
        '\'',
        '"',
    };

    public static IReadOnlyList<(int start, int length, string url)> Find(string line)
    {
        var results = new List<(int start, int length, string url)>();
        foreach (Match match in UrlPattern().Matches(line))
        {
            var url = match.Value.TrimEnd(trailingPunctuation);
            if (url.Length == 0)
            {
                continue;
            }
            results.Add((match.Index, url.Length, url));
        }
        return results;
    }
}
