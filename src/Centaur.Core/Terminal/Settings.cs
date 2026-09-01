using System.Text.Json;
using System.Text.Json.Serialization;

namespace Centaur.Core.Terminal;

public enum StartDirectoryMode
{
    LastFolder,
    HomeFolder,
    SpecificFolder,
}

public enum CursorStyle
{
    Block,
    Underline,
    Bar,
}

public enum BellMode
{
    Off,
    Sound,
    Flash,
}

/// <summary>
/// Every user-configurable option, persisted as one flat JSON document.
///
/// The shape stays flat and the key names never change, so a settings file written by an older
/// build still loads: options it predates simply fall back to the value that was hardcoded when
/// it was written. Everything numeric is clamped on the way in, because a hand-edited file must
/// not be able to produce an unreadable window.
/// </summary>
public class Settings
{
    public const string DefaultThemeId = "catppuccin-macchiato";
    public const string DefaultShellCommand = "powershell.exe";

    readonly JsonFileStore<SettingsData> store;

    static readonly JsonSerializerOptions jsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// Raised after a successful save, carrying the id of the setting that changed (the same id
    /// the settings page registers a descriptor under), or an empty string for a bulk save.
    /// </summary>
    public event Action<string>? Changed;

    public StartDirectoryMode StartDirectory { get; set; } = StartDirectoryMode.LastFolder;
    public string SpecificFolder { get; set; } = "";
    public string LastFolder { get; set; } = "";
    public string ShellCommand { get; set; } = DefaultShellCommand;
    public int ScrollbackLines { get; set; } = 10000;
    public BellMode Bell { get; set; } = BellMode.Off;

    public string ThemeId { get; set; } = DefaultThemeId;
    public double FontSize { get; set; } = 14;
    public double LineHeight { get; set; } = 1.2;
    public CursorStyle CursorStyle { get; set; } = CursorStyle.Block;
    public bool CursorBlink { get; set; }
    public double WindowOpacity { get; set; } = 1.0;
    public int ContentPadding { get; set; } = 8;

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
        ShellCommand = Fallback(data.ShellCommand, DefaultShellCommand);
        ScrollbackLines = Clamp(data.ScrollbackLines ?? ScrollbackLines, 0, 200000);
        Bell = data.Bell ?? Bell;

        ThemeId = Fallback(data.ThemeId, DefaultThemeId);
        FontSize = Clamp(data.FontSize ?? FontSize, 8, 48);
        LineHeight = Clamp(data.LineHeight ?? LineHeight, 1.0, 2.0);
        CursorStyle = data.CursorStyle ?? CursorStyle;
        CursorBlink = data.CursorBlink ?? CursorBlink;
        WindowOpacity = Clamp(data.WindowOpacity ?? WindowOpacity, 0.5, 1.0);
        ContentPadding = Clamp(data.ContentPadding ?? ContentPadding, 0, 64);
    }

    /// <summary>
    /// Writes the document and announces the change. The settings page has no apply or cancel
    /// step, so every edit calls this with its own id and the change reaches the running terminal
    /// immediately.
    /// </summary>
    public void Save(string changedId = "")
    {
        store.Save(
            new SettingsData
            {
                StartDirectory = StartDirectory,
                SpecificFolder = SpecificFolder,
                LastFolder = LastFolder,
                ShellCommand = ShellCommand,
                ScrollbackLines = ScrollbackLines,
                Bell = Bell,
                ThemeId = ThemeId,
                FontSize = FontSize,
                LineHeight = LineHeight,
                CursorStyle = CursorStyle,
                CursorBlink = CursorBlink,
                WindowOpacity = WindowOpacity,
                ContentPadding = ContentPadding,
            }
        );

        Changed?.Invoke(changedId);
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
        Save(SettingIds.LastFolder);
    }

    static string Fallback(string? value, string defaultValue) =>
        string.IsNullOrWhiteSpace(value) ? defaultValue : value;

    static double Clamp(double value, double min, double max) => Math.Clamp(value, min, max);

    static int Clamp(int value, int min, int max) => Math.Clamp(value, min, max);

    /// <summary>
    /// Every option added after the first release is nullable so that its absence from an older
    /// file is distinguishable from a stored zero, and falls back to the property default rather
    /// than to <c>default(T)</c>.
    /// </summary>
    sealed class SettingsData
    {
        public StartDirectoryMode StartDirectory { get; set; }
        public string? SpecificFolder { get; set; }
        public string? LastFolder { get; set; }
        public string? ShellCommand { get; set; }
        public int? ScrollbackLines { get; set; }
        public BellMode? Bell { get; set; }
        public string? ThemeId { get; set; }
        public double? FontSize { get; set; }
        public double? LineHeight { get; set; }
        public CursorStyle? CursorStyle { get; set; }
        public bool? CursorBlink { get; set; }
        public double? WindowOpacity { get; set; }
        public int? ContentPadding { get; set; }
    }
}
