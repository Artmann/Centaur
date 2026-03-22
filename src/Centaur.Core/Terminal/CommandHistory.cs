using System.Text.Json;

namespace Centaur.Core.Terminal;

public class CommandHistory
{
    readonly List<string> commands = [];
    readonly string? filePath;
    const int maxEntries = 1000;

    public CommandHistory(string? filePath = null)
    {
        this.filePath = filePath;
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
        if (filePath == null || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var loaded = JsonSerializer.Deserialize<List<string>>(json);
            if (loaded != null)
            {
                commands.Clear();
                commands.AddRange(loaded);
            }
        }
        catch
        {
            // Corrupt or unreadable file — start with empty history
        }
    }

    public void Save()
    {
        if (filePath == null)
        {
            return;
        }

        var dir = Path.GetDirectoryName(filePath);
        if (dir != null && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(commands);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
