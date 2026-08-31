using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class SettingsTests : TempDirectory
{
    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        var settings = new Settings(TempFile("missing.json"));
        settings.Load();

        Assert.Equal(StartDirectoryMode.LastFolder, settings.StartDirectory);
        Assert.Equal("", settings.SpecificFolder);
        Assert.Equal("", settings.LastFolder);
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        var path = TempFile("settings.json");
        var settings = new Settings(path);
        settings.StartDirectory = StartDirectoryMode.SpecificFolder;
        settings.SpecificFolder = @"C:\Projects";
        settings.LastFolder = @"C:\Users\Test";
        settings.Save();

        var loaded = new Settings(path);
        loaded.Load();

        Assert.Equal(StartDirectoryMode.SpecificFolder, loaded.StartDirectory);
        Assert.Equal(@"C:\Projects", loaded.SpecificFolder);
        Assert.Equal(@"C:\Users\Test", loaded.LastFolder);
    }

    [Fact]
    public void GetStartingDirectory_LastFolder_ReturnsLastFolder()
    {
        var settings = new Settings();
        settings.StartDirectory = StartDirectoryMode.LastFolder;
        settings.LastFolder = TempDir;

        Assert.Equal(TempDir, settings.GetStartingDirectory());
    }

    [Fact]
    public void GetStartingDirectory_LastFolder_WhenEmpty_ReturnsNull()
    {
        var settings = new Settings();
        settings.StartDirectory = StartDirectoryMode.LastFolder;
        settings.LastFolder = "";

        Assert.Null(settings.GetStartingDirectory());
    }

    [Fact]
    public void GetStartingDirectory_HomeFolder_ReturnsUserProfile()
    {
        var settings = new Settings();
        settings.StartDirectory = StartDirectoryMode.HomeFolder;

        var expected = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.Equal(expected, settings.GetStartingDirectory());
    }

    [Fact]
    public void GetStartingDirectory_SpecificFolder_WhenExists_ReturnsPath()
    {
        var settings = new Settings();
        settings.StartDirectory = StartDirectoryMode.SpecificFolder;
        settings.SpecificFolder = TempDir;

        Assert.Equal(TempDir, settings.GetStartingDirectory());
    }

    [Fact]
    public void GetStartingDirectory_SpecificFolder_WhenMissing_ReturnsNull()
    {
        var settings = new Settings();
        settings.StartDirectory = StartDirectoryMode.SpecificFolder;
        settings.SpecificFolder = @"C:\NonExistent\Path\12345";

        Assert.Null(settings.GetStartingDirectory());
    }

    [Fact]
    public void UpdateLastFolder_PersistsToDisk()
    {
        var path = TempFile("settings.json");
        var settings = new Settings(path);
        settings.UpdateLastFolder(TempDir);

        var loaded = new Settings(path);
        loaded.Load();

        Assert.Equal(TempDir, loaded.LastFolder);
    }

    [Fact]
    public void Load_CorruptFile_UsesDefaults()
    {
        var path = TempFile("settings.json");
        File.WriteAllText(path, "not valid json {{{");

        var settings = new Settings(path);
        settings.Load();

        Assert.Equal(StartDirectoryMode.LastFolder, settings.StartDirectory);
        Assert.Equal("", settings.SpecificFolder);
    }

    [Fact]
    public void Load_MissingFile_UsesAppearanceDefaults()
    {
        var settings = new Settings(TempFile("missing.json"));
        settings.Load();

        Assert.Equal("catppuccin-macchiato", settings.ThemeId);
        Assert.Equal(14, settings.FontSize);
        Assert.Equal(1.2, settings.LineHeight);
        Assert.Equal(CursorStyle.Block, settings.CursorStyle);
        Assert.False(settings.CursorBlink);
        Assert.Equal(1.0, settings.WindowOpacity);
        Assert.Equal(8, settings.ContentPadding);
    }

    [Fact]
    public void Load_MissingFile_UsesGeneralDefaults()
    {
        var settings = new Settings(TempFile("missing.json"));
        settings.Load();

        Assert.Equal("powershell.exe", settings.ShellCommand);
        Assert.Equal(10000, settings.ScrollbackLines);
        Assert.Equal(BellMode.Off, settings.Bell);
    }

    [Fact]
    public void Save_And_Load_RoundTripsEverySetting()
    {
        var path = TempFile("settings.json");
        var settings = new Settings(path)
        {
            StartDirectory = StartDirectoryMode.HomeFolder,
            ShellCommand = "pwsh.exe",
            ScrollbackLines = 5000,
            Bell = BellMode.Visual,
            ThemeId = "catppuccin-latte",
            FontSize = 18,
            LineHeight = 1.4,
            CursorStyle = CursorStyle.Bar,
            CursorBlink = true,
            WindowOpacity = 0.8,
            ContentPadding = 16,
        };
        settings.Save();

        var loaded = new Settings(path);
        loaded.Load();

        Assert.Equal(StartDirectoryMode.HomeFolder, loaded.StartDirectory);
        Assert.Equal("pwsh.exe", loaded.ShellCommand);
        Assert.Equal(5000, loaded.ScrollbackLines);
        Assert.Equal(BellMode.Visual, loaded.Bell);
        Assert.Equal("catppuccin-latte", loaded.ThemeId);
        Assert.Equal(18, loaded.FontSize);
        Assert.Equal(1.4, loaded.LineHeight);
        Assert.Equal(CursorStyle.Bar, loaded.CursorStyle);
        Assert.True(loaded.CursorBlink);
        Assert.Equal(0.8, loaded.WindowOpacity);
        Assert.Equal(16, loaded.ContentPadding);
    }

    /// <summary>
    /// Settings files written before the appearance options existed hold only the three start
    /// directory keys. They have to keep working, with the new options falling back to the values
    /// that were hardcoded at the time the file was written.
    /// </summary>
    [Fact]
    public void Load_LegacyFile_KeepsStartDirectoryAndDefaultsTheRest()
    {
        var path = TempFile("settings.json");
        File.WriteAllText(
            path,
            """
            {
              "StartDirectory": "SpecificFolder",
              "SpecificFolder": "C:\\Projects",
              "LastFolder": "C:\\Users\\Test"
            }
            """
        );

        var settings = new Settings(path);
        settings.Load();

        Assert.Equal(StartDirectoryMode.SpecificFolder, settings.StartDirectory);
        Assert.Equal(@"C:\Projects", settings.SpecificFolder);
        Assert.Equal(@"C:\Users\Test", settings.LastFolder);
        Assert.Equal("catppuccin-macchiato", settings.ThemeId);
        Assert.Equal(14, settings.FontSize);
        Assert.Equal("powershell.exe", settings.ShellCommand);
    }

    [Theory]
    [InlineData(0, 8)]
    [InlineData(4, 8)]
    [InlineData(8, 8)]
    [InlineData(14, 14)]
    [InlineData(48, 48)]
    [InlineData(200, 48)]
    public void Load_ClampsFontSize(double stored, double expected)
    {
        Assert.Equal(expected, LoadWith($"\"FontSize\": {stored}").FontSize);
    }

    [Theory]
    [InlineData(0.1, 1.0)]
    [InlineData(1.2, 1.2)]
    [InlineData(9.0, 2.0)]
    public void Load_ClampsLineHeight(double stored, double expected)
    {
        Assert.Equal(expected, LoadWith($"\"LineHeight\": {stored}").LineHeight);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(10000, 10000)]
    [InlineData(999999, 200000)]
    public void Load_ClampsScrollbackLines(int stored, int expected)
    {
        Assert.Equal(expected, LoadWith($"\"ScrollbackLines\": {stored}").ScrollbackLines);
    }

    [Theory]
    [InlineData(0.0, 0.5)]
    [InlineData(0.75, 0.75)]
    [InlineData(3.0, 1.0)]
    public void Load_ClampsWindowOpacity(double stored, double expected)
    {
        Assert.Equal(expected, LoadWith($"\"WindowOpacity\": {stored}").WindowOpacity);
    }

    [Theory]
    [InlineData(-10, 0)]
    [InlineData(8, 8)]
    [InlineData(500, 64)]
    public void Load_ClampsContentPadding(int stored, int expected)
    {
        Assert.Equal(expected, LoadWith($"\"ContentPadding\": {stored}").ContentPadding);
    }

    [Fact]
    public void Load_BlankShellCommand_FallsBackToDefault()
    {
        Assert.Equal("powershell.exe", LoadWith("\"ShellCommand\": \"   \"").ShellCommand);
    }

    [Fact]
    public void Load_BlankThemeId_FallsBackToDefault()
    {
        Assert.Equal("catppuccin-macchiato", LoadWith("\"ThemeId\": \"\"").ThemeId);
    }

    [Fact]
    public void Save_RaisesChanged()
    {
        var settings = new Settings(TempFile("settings.json"));
        var seen = new List<string>();
        settings.Changed += id => seen.Add(id);

        settings.FontSize = 20;
        settings.Save("appearance.fontSize");

        Assert.Equal(["appearance.fontSize"], seen);
    }

    [Fact]
    public void Save_WithoutAnId_RaisesChangedWithAnEmptyId()
    {
        var settings = new Settings(TempFile("settings.json"));
        var seen = new List<string>();
        settings.Changed += id => seen.Add(id);

        settings.Save();

        Assert.Equal([""], seen);
    }

    Settings LoadWith(string jsonProperty)
    {
        var path = TempFile("settings.json");
        File.WriteAllText(path, $"{{ {jsonProperty} }}");

        var settings = new Settings(path);
        settings.Load();

        return settings;
    }
}
