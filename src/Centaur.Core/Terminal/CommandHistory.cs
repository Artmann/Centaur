namespace Centaur.Core.Terminal;

public class CommandHistory
{
    readonly List<string> commands = [];
    readonly JsonFileStore<List<string>> store;
    const int maxEntries = 1000;

    public CommandHistory(string? filePath = null, Action<Exception>? onError = null)
    {
        store = new JsonFileStore<List<string>>(filePath, onError: onError);
    }

    public void Add(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return;
        }

        commands.RemoveAll(c => c.Equals(command, StringComparison.OrdinalIgnoreCase));
        commands.Add(command);

        if (commands.Count > maxEntries)
        {
            commands.RemoveAt(0);
        }
    }

    public string? FindMatch(string prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return null;
        }

        return commands.LastOrDefault(c =>
            c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && c.Length > prefix.Length
        );
    }

    public IReadOnlyList<string> GetAll() => commands.AsReadOnly();

    public void Load()
    {
        var loaded = store.Load();
        if (loaded == null)
        {
            return;
        }

        commands.Clear();
        commands.AddRange(loaded);
    }

    public void Save()
    {
        store.Save(commands);
    }
}
