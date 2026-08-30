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
    readonly JsonFileStore<SettingsData> store;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public StartDirectoryMode StartDirectory { get; set; } = StartDirectoryMode.LastFolder;
    public string SpecificFolder { get; set; } = "";
    public string LastFolder { get; set; } = "";

    public Settings(string? filePath = null, Action<Exception>? onError = null)
    {
        store = new JsonFileStore<SettingsData>(filePath, jsonOptions, onError);
    }

    public void Load()
    {
        var data = store.Load();
        if (data == null)
        {
            return;
        }

        StartDirectory = data.StartDirectory;
        SpecificFolder = data.SpecificFolder ?? "";
        LastFolder = data.LastFolder ?? "";
    }

    public void Save()
    {
        store.Save(
            new SettingsData
            {
                StartDirectory = StartDirectory,
                SpecificFolder = SpecificFolder,
                LastFolder = LastFolder,
            }
        );
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
