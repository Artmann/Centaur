using System.Text.Json;

namespace Centaur.Core.Terminal;

/// <summary>
/// Loads and atomically saves a JSON document backed by a single file.
/// A null path makes the store a no-op, which is how the in-memory defaults are used in tests.
/// </summary>
public sealed class JsonFileStore<T>
    where T : class
{
    readonly string? filePath;
    readonly JsonSerializerOptions? jsonOptions;
    readonly Action<Exception>? onError;

    public JsonFileStore(
        string? filePath,
        JsonSerializerOptions? jsonOptions = null,
        Action<Exception>? onError = null
    )
    {
        this.filePath = filePath;
        this.jsonOptions = jsonOptions;
        this.onError = onError;
    }

    /// <summary>
    /// Returns the stored document, or null when there is no file yet or it could not be read.
    /// Read failures are reported through the onError callback rather than thrown, so a corrupt
    /// file degrades to defaults instead of taking the app down.
    /// </summary>
    public T? Load()
    {
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(filePath);
            return JsonSerializer.Deserialize<T>(json, jsonOptions);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
            return null;
        }
    }

    /// <summary>
    /// Writes through a temp file so an interrupted save cannot leave a truncated document behind.
    /// </summary>
    public void Save(T value)
    {
        if (filePath == null)
        {
            return;
        }

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (dir != null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var tempPath = filePath + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(value, jsonOptions));
            File.Move(tempPath, filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            onError?.Invoke(ex);
        }
    }
}
