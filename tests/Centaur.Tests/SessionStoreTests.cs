using Avalonia.Layout;
using Centaur.App;
using Xunit;

namespace Centaur.Tests;

public class SessionStoreTests : TempDirectory
{
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
        var path = TempFile("session.json");
        var store = new SessionStore(path) { Data = SplitTabSession() };
        store.Save();

        var loaded = new SessionStore(path);
        loaded.Load();

        AssertMatchesSplitTabSession(loaded.Data);
    }

    /// <summary>One maximized window holding a single tab split horizontally in two — every
    /// field the store persists, each with a value distinguishable from its default.</summary>
    static SessionData SplitTabSession() =>
        new()
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

    static void AssertMatchesSplitTabSession(SessionData data)
    {
        Assert.Equal(1, data.ActiveTabIndex);
        Assert.Equal(10, data.WindowX);
        Assert.Equal(20, data.WindowY);
        Assert.Equal(1600, data.WindowWidth);
        Assert.Equal(900, data.WindowHeight);
        Assert.True(data.WindowMaximized);
        Assert.Single(data.Tabs);

        var root = data.Tabs[0].Root;
        Assert.Equal("Tab 1", data.Tabs[0].Title);
        Assert.True(root.IsSplit);
        Assert.Equal(Orientation.Horizontal, root.Orientation);
        Assert.Equal(0.35, root.Ratio);
        Assert.Equal(@"C:\a", root.First!.WorkingDirectory);
        Assert.Equal(@"C:\b", root.Second!.WorkingDirectory);
    }

    [Fact]
    public void Load_CorruptFile_UsesDefaults()
    {
        var path = TempFile("session.json");
        File.WriteAllText(path, "not valid json {{{");

        var store = new SessionStore(path);
        store.Load();

        Assert.Empty(store.Data.Tabs);
        Assert.Equal(1280, store.Data.WindowWidth);
    }
}
