using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class FuzzyMatcherTests
{
    [Fact]
    public void Match_ExactMatch_ReturnsAllIndices()
    {
        var result = FuzzyMatcher.Match("git", "git");

        Assert.NotNull(result);
        Assert.Equal(new[] { 0, 1, 2 }, result.MatchedIndices);
    }

    [Fact]
    public void Match_PrefixMatch_ReturnsCorrectIndices()
    {
        var result = FuzzyMatcher.Match("git", "git commit");

        Assert.NotNull(result);
        Assert.Equal(new[] { 0, 1, 2 }, result.MatchedIndices);
    }

    [Fact]
    public void Match_NonContiguousMatch_ReturnsCorrectIndices()
    {
        var result = FuzzyMatcher.Match("gcp", "git cherry-pick");

        Assert.NotNull(result);
        Assert.Equal(3, result.MatchedIndices.Count);
        Assert.Equal(0, result.MatchedIndices[0]); // g
        Assert.Equal(4, result.MatchedIndices[1]); // c
        Assert.Equal(11, result.MatchedIndices[2]); // p
    }

    [Fact]
    public void Match_NoMatch_ReturnsNull()
    {
        var result = FuzzyMatcher.Match("xyz", "git commit");

        Assert.Null(result);
    }

    [Fact]
    public void Match_EmptyPattern_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Match("", "git commit"));
    }

    [Fact]
    public void Match_EmptyCandidate_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Match("git", ""));
    }

    [Fact]
    public void Match_NullPattern_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Match(null!, "git"));
    }

    [Fact]
    public void Match_NullCandidate_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Match("git", null!));
    }

    [Fact]
    public void Match_CaseInsensitive()
    {
        var result = FuzzyMatcher.Match("GIT", "git commit");

        Assert.NotNull(result);
        Assert.Equal(new[] { 0, 1, 2 }, result.MatchedIndices);
    }

    [Fact]
    public void Match_ConsecutiveMatchesScoreHigherThanScattered()
    {
        // "git" in "git status" is consecutive (indices 0,1,2)
        var consecutive = FuzzyMatcher.Match("git", "git status");
        // "git" in "magxiety" is scattered mid-word (no boundary bonuses)
        var scattered = FuzzyMatcher.Match("git", "magxiety");

        Assert.NotNull(consecutive);
        Assert.NotNull(scattered);
        Assert.True(consecutive.Score > scattered.Score);
    }

    [Fact]
    public void Match_WordBoundaryMatchScoresHigher()
    {
        // "c" at word boundary (start of "commit") vs middle of word
        var boundary = FuzzyMatcher.Match("c", "git commit");
        var middle = FuzzyMatcher.Match("c", "success");

        Assert.NotNull(boundary);
        Assert.NotNull(middle);
        Assert.True(boundary.Score > middle.Score);
    }

    [Fact]
    public void Match_StartOfStringScoresHigher()
    {
        var atStart = FuzzyMatcher.Match("g", "git commit");
        var notAtStart = FuzzyMatcher.Match("g", "log");

        Assert.NotNull(atStart);
        Assert.NotNull(notAtStart);
        Assert.True(atStart.Score > notAtStart.Score);
    }

    [Fact]
    public void Match_PatternLongerThanCandidate_ReturnsNull()
    {
        Assert.Null(FuzzyMatcher.Match("longpattern", "short"));
    }

    [Fact]
    public void Match_SpecialCharacters()
    {
        var result = FuzzyMatcher.Match("--no", "git commit --no-verify");

        Assert.NotNull(result);
    }

    [Fact]
    public void Match_SingleCharacter()
    {
        var result = FuzzyMatcher.Match("g", "git");

        Assert.NotNull(result);
        Assert.Single(result.MatchedIndices);
        Assert.Equal(0, result.MatchedIndices[0]);
    }
}
