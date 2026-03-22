namespace Centaur.Core.Terminal;

public record FilteredCommand(string Command, FuzzyMatchResult? MatchResult);

public class ReverseSearchState
{
    List<FilteredCommand> filteredResults = [];

    public string Query { get; private set; } = "";
    public IReadOnlyList<FilteredCommand> FilteredResults => filteredResults;
    public int SelectedIndex { get; private set; } = -1;
    public int TotalCount { get; private set; }

    public FilteredCommand? SelectedCommand =>
        SelectedIndex >= 0 && SelectedIndex < filteredResults.Count
            ? filteredResults[SelectedIndex]
            : null;

    public void UpdateQuery(IReadOnlyList<string> commands, string query)
    {
        Query = query;
        TotalCount = commands.Count;

        if (string.IsNullOrEmpty(query))
        {
            filteredResults = commands.Select(c => new FilteredCommand(c, null)).ToList();
        }
        else
        {
            filteredResults = commands
                .Select(c => (Command: c, Result: FuzzyMatcher.Match(query, c)))
                .Where(x => x.Result != null)
                .OrderBy(x => x.Result!.Score)
                .Select(x => new FilteredCommand(x.Command, x.Result))
                .ToList();
        }

        SelectedIndex = filteredResults.Count > 0 ? filteredResults.Count - 1 : -1;
    }

    public void MoveSelection(int delta)
    {
        if (filteredResults.Count == 0)
        {
            return;
        }

        SelectedIndex =
            ((SelectedIndex + delta) % filteredResults.Count + filteredResults.Count)
            % filteredResults.Count;
    }

    public void Reset()
    {
        Query = "";
        filteredResults = [];
        SelectedIndex = -1;
        TotalCount = 0;
    }
}
