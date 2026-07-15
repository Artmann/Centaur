using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centaur.App;

public class SessionStore
{
    readonly string? filePath;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SessionData Data { get; set; } = new();

    public SessionStore(string? filePath = null)
    {
        this.filePath = filePath;
    }

    public void Load()
    {
        if (filePath == null || !File.Exists(filePath))
        {
            return;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            var data = JsonSerializer.Deserialize<SessionData>(json, jsonOptions);
            if (data != null)
            {
                Data = data;
            }
        }
        catch
        {
            // Corrupt or unreadable file — use defaults
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
        var json = JsonSerializer.Serialize(Data, jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }
}
