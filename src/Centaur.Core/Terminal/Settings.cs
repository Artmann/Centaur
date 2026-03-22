using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centaur.Core.Terminal;

public enum StartDirectoryMode
{
    LastFolder,
    HomeFolder,
    SpecificFolder,
}

public class Settings
{
    readonly string? filePath;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StartDirectoryMode StartDirectory { get; set; } = StartDirectoryMode.LastFolder;
    public string SpecificFolder { get; set; } = "";
    public string LastFolder { get; set; } = "";

    public Settings(string? filePath = null)
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
            var data = JsonSerializer.Deserialize<SettingsData>(json, jsonOptions);
            if (data != null)
            {
                StartDirectory = data.StartDirectory;
                SpecificFolder = data.SpecificFolder ?? "";
                LastFolder = data.LastFolder ?? "";
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

        var data = new SettingsData
        {
            StartDirectory = StartDirectory,
            SpecificFolder = SpecificFolder,
            LastFolder = LastFolder,
        };

        var tempPath = filePath + ".tmp";
        var json = JsonSerializer.Serialize(data, jsonOptions);
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, filePath, overwrite: true);
    }

    public string? GetStartingDirectory()
    {
        return StartDirectory switch
        {
            StartDirectoryMode.LastFolder => string.IsNullOrEmpty(LastFolder) ? null : LastFolder,
            StartDirectoryMode.HomeFolder => Environment.GetFolderPath(
                Environment.SpecialFolder.UserProfile
            ),
            StartDirectoryMode.SpecificFolder => Directory.Exists(SpecificFolder)
                ? SpecificFolder
                : null,
            _ => null,
        };
    }

    public void UpdateLastFolder(string path)
    {
        LastFolder = path;
        Save();
    }

    sealed class SettingsData
    {
        public StartDirectoryMode StartDirectory { get; set; }
        public string? SpecificFolder { get; set; }
        public string? LastFolder { get; set; }
    }
}
