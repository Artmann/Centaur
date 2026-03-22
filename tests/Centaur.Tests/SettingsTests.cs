using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class SettingsTests : IDisposable
{
    readonly string tempDir;

    public SettingsTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "centaur-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    string TempFile(string name = "settings.json") => Path.Combine(tempDir, name);

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
        var path = TempFile();
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
        settings.LastFolder = tempDir;

        Assert.Equal(tempDir, settings.GetStartingDirectory());
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
        settings.SpecificFolder = tempDir;

        Assert.Equal(tempDir, settings.GetStartingDirectory());
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
        var path = TempFile();
        var settings = new Settings(path);
        settings.UpdateLastFolder(tempDir);

        var loaded = new Settings(path);
        loaded.Load();

        Assert.Equal(tempDir, loaded.LastFolder);
    }

    [Fact]
    public void Load_CorruptFile_UsesDefaults()
    {
        var path = TempFile();
        File.WriteAllText(path, "not valid json {{{");

        var settings = new Settings(path);
        settings.Load();

        Assert.Equal(StartDirectoryMode.LastFolder, settings.StartDirectory);
        Assert.Equal("", settings.SpecificFolder);
    }
}
