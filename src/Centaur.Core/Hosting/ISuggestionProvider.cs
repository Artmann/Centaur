namespace Centaur.Core.Hosting;

public interface ISuggestionProvider : IProvider
{
    string? GetSuggestion(string currentInput);
    void RecordCommand(string command);
}
