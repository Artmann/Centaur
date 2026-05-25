using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// URL detection within a rendered line (Phase 3 "URL detection + click").
///
/// Centaur-native: there is no direct single-source Ghostty equivalent at this
/// layer (Ghostty's URL matching lives in its regex/config layer), so this is a
/// small hand-written contract. The detector scans a line of text and returns
/// the span and value of each http(s) URL, excluding trailing punctuation.
///
/// Intended API (not yet implemented):
///   static class UrlDetector {
///       static IReadOnlyList&lt;(int start, int length, string url)&gt; Find(string line);
///   }
/// </summary>
public class UrlDetectorTests
{
    [Fact]
    public void Find_SingleHttpUrl_ReturnsSpan()
    {
        var line = "go to http://example.com now";
        var matches = UrlDetector.Find(line);

        var m = Assert.Single(matches);
        Assert.Equal("http://example.com", m.url);
        Assert.Equal(line.IndexOf("http", StringComparison.Ordinal), m.start);
        Assert.Equal("http://example.com".Length, m.length);
    }

    [Fact]
    public void Find_HttpsUrl_Detected()
    {
        var matches = UrlDetector.Find("https://example.com/path?q=1");
        var m = Assert.Single(matches);
        Assert.Equal("https://example.com/path?q=1", m.url);
    }

    [Fact]
    public void Find_MultipleUrls_ReturnsAll()
    {
        var matches = UrlDetector.Find("http://a.com and http://b.com");
        Assert.Equal(2, matches.Count);
        Assert.Equal("http://a.com", matches[0].url);
        Assert.Equal("http://b.com", matches[1].url);
    }

    [Fact]
    public void Find_NoUrl_ReturnsEmpty()
    {
        Assert.Empty(UrlDetector.Find("just some plain text, no links"));
    }

    [Fact]
    public void Find_TrailingPunctuation_Excluded()
    {
        // A sentence-ending period must not be part of the URL.
        var matches = UrlDetector.Find("see http://example.com.");
        var m = Assert.Single(matches);
        Assert.Equal("http://example.com", m.url);
    }
}
