using Avalonia.Layout;
using Centaur.App;
using Xunit;

namespace Centaur.Tests;

public class SessionStoreTests : IDisposable
{
    readonly string tempDir;

    public SessionStoreTests()
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

    string TempFile(string name = "session.json") => Path.Combine(tempDir, name);

    [Fact]
    public void Load_MissingFile_UsesDefaults()
    {
        var store = new SessionStore(TempFile("missing.json"));
        store.Load();

        Assert.Empty(store.Data.Tabs);
        Assert.Equal(1280, store.Data.WindowWidth);
        Assert.Equal(800, store.Data.WindowHeight);
        Assert.False(store.Data.WindowMaximized);
    }

    [Fact]
    public void Save_And_Load_RoundTrips()
    {
        var path = TempFile();
        var store = new SessionStore(path);
        store.Data = new SessionData
        {
            ActiveTabIndex = 1,
            WindowX = 10,
            WindowY = 20,
            WindowWidth = 1600,
            WindowHeight = 900,
            WindowMaximized = true,
            Tabs =
            [
                new SessionTab
                {
                    Title = "Tab 1",
                    Root = new SessionNode
                    {
                        IsSplit = true,
                        Orientation = Orientation.Horizontal,
                        Ratio = 0.35,
                        First = new SessionNode { IsSplit = false, WorkingDirectory = @"C:\a" },
                        Second = new SessionNode { IsSplit = false, WorkingDirectory = @"C:\b" },
                    },
                },
            ],
        };
        store.Save();

        var loaded = new SessionStore(path);
        loaded.Load();

        Assert.Equal(1, loaded.Data.ActiveTabIndex);
        Assert.Equal(10, loaded.Data.WindowX);
        Assert.Equal(20, loaded.Data.WindowY);
        Assert.Equal(1600, loaded.Data.WindowWidth);
        Assert.Equal(900, loaded.Data.WindowHeight);
        Assert.True(loaded.Data.WindowMaximized);
        Assert.Single(loaded.Data.Tabs);
        Assert.Equal("Tab 1", loaded.Data.Tabs[0].Title);
        Assert.True(loaded.Data.Tabs[0].Root.IsSplit);
        Assert.Equal(Orientation.Horizontal, loaded.Data.Tabs[0].Root.Orientation);
        Assert.Equal(0.35, loaded.Data.Tabs[0].Root.Ratio);
        Assert.Equal(@"C:\a", loaded.Data.Tabs[0].Root.First!.WorkingDirectory);
        Assert.Equal(@"C:\b", loaded.Data.Tabs[0].Root.Second!.WorkingDirectory);
    }

    [Fact]
    public void Load_CorruptFile_UsesDefaults()
    {
        var path = TempFile();
        File.WriteAllText(path, "not valid json {{{");

        var store = new SessionStore(path);
        store.Load();

        Assert.Empty(store.Data.Tabs);
        Assert.Equal(1280, store.Data.WindowWidth);
    }
}
