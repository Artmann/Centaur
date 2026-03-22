using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ReverseSearchStateTests
{
    [Fact]
    public void UpdateQuery_EmptyQuery_ReturnsAllCommands()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "git add .", "git commit", "npm install" };

        state.UpdateQuery(commands, "");

        Assert.Equal(3, state.FilteredResults.Count);
        Assert.Equal(3, state.TotalCount);
    }

    [Fact]
    public void UpdateQuery_EmptyQuery_ResultsHaveNullMatchResult()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "git add ." };

        state.UpdateQuery(commands, "");

        Assert.Null(state.FilteredResults[0].MatchResult);
    }

    [Fact]
    public void UpdateQuery_FiltersWithFuzzyMatch()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "git add .", "npm install", "git commit" };

        state.UpdateQuery(commands, "git");

        Assert.Equal(2, state.FilteredResults.Count);
        Assert.All(
            state.FilteredResults,
            r => Assert.Contains("git", r.Command, StringComparison.OrdinalIgnoreCase)
        );
    }

    [Fact]
    public void UpdateQuery_ResultsSortedByScore_BestMatchLast()
    {
        var state = new ReverseSearchState();
        // "gc" matches "git cherry-pick" (scattered) and "gc collect" (consecutive at start)
        var commands = new List<string> { "git cherry-pick", "gc collect" };

        state.UpdateQuery(commands, "gc");

        // Best match (highest score) should be last (closest to input bar)
        Assert.Equal(2, state.FilteredResults.Count);
        var lastResult = state.FilteredResults[^1];
        var firstResult = state.FilteredResults[0];
        Assert.True(lastResult.MatchResult!.Score >= firstResult.MatchResult!.Score);
    }

    [Fact]
    public void UpdateQuery_NoMatches_ReturnsEmpty()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "git add .", "git commit" };

        state.UpdateQuery(commands, "xyz");

        Assert.Empty(state.FilteredResults);
    }

    [Fact]
    public void UpdateQuery_ResetsSelectionToLastItem()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "git add .", "git commit", "git push" };

        state.UpdateQuery(commands, "git");

        Assert.Equal(state.FilteredResults.Count - 1, state.SelectedIndex);
    }

    [Fact]
    public void UpdateQuery_NoMatches_SelectedIndexIsNegativeOne()
    {
        var state = new ReverseSearchState();

        state.UpdateQuery(new List<string> { "git add" }, "xyz");

        Assert.Equal(-1, state.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_NegativeDelta_MovesUp()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "a", "b", "c" };
        state.UpdateQuery(commands, "");
        // Selection starts at last item (index 2)

        state.MoveSelection(-1);

        Assert.Equal(1, state.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_PositiveDelta_MovesDown()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "a", "b", "c" };
        state.UpdateQuery(commands, "");
        state.MoveSelection(-1); // now at index 1

        state.MoveSelection(1);

        Assert.Equal(2, state.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_WrapsFromTopToBottom()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "a", "b", "c" };
        state.UpdateQuery(commands, "");
        // Move to index 0
        state.MoveSelection(-1);
        state.MoveSelection(-1);

        // Wrap around
        state.MoveSelection(-1);

        Assert.Equal(2, state.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_WrapsFromBottomToTop()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "a", "b", "c" };
        state.UpdateQuery(commands, "");
        // Selection at last item (index 2)

        state.MoveSelection(1);

        Assert.Equal(0, state.SelectedIndex);
    }

    [Fact]
    public void MoveSelection_EmptyResults_StaysAtNegativeOne()
    {
        var state = new ReverseSearchState();
        state.UpdateQuery(new List<string>(), "");

        state.MoveSelection(1);

        Assert.Equal(-1, state.SelectedIndex);
    }

    [Fact]
    public void SelectedCommand_ReturnsCorrectCommand()
    {
        var state = new ReverseSearchState();
        var commands = new List<string> { "a", "b", "c" };
        state.UpdateQuery(commands, "");

        Assert.NotNull(state.SelectedCommand);
        Assert.Equal("c", state.SelectedCommand!.Command);
    }

    [Fact]
    public void SelectedCommand_ReturnsNull_WhenEmpty()
    {
        var state = new ReverseSearchState();
        state.UpdateQuery(new List<string>(), "");

        Assert.Null(state.SelectedCommand);
    }

    [Fact]
    public void Reset_ClearsState()
    {
        var state = new ReverseSearchState();
        state.UpdateQuery(new List<string> { "a", "b" }, "a");

        state.Reset();

        Assert.Empty(state.FilteredResults);
        Assert.Equal("", state.Query);
        Assert.Equal(-1, state.SelectedIndex);
        Assert.Equal(0, state.TotalCount);
    }
}
