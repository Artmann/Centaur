using System.Text.Json;
using System.Text.Json.Serialization;
using Centaur.Core.Terminal;

namespace Centaur.App;

public class SessionStore
{
    readonly JsonFileStore<SessionData> store;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public SessionData Data { get; set; } = new();

    public SessionStore(string? filePath = null, Action<Exception>? onError = null)
    {
        store = new JsonFileStore<SessionData>(filePath, jsonOptions, onError);
    }

    public void Load()
    {
        var data = store.Load();
        if (data != null)
        {
            Data = data;
        }
    }

    public void Save()
    {
        store.Save(Data);
    }
}
