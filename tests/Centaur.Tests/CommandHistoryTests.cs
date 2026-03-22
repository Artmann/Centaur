using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class CommandHistoryTests : IDisposable
{
    readonly string tempDir;

    public CommandHistoryTests()
    {
        tempDir = Path.Combine(Path.GetTempPath(), "centaur-test-" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void FindMatch_ReturnsMostRecentMatchingCommand()
    {
        var history = new CommandHistory();
        history.Add("git add .");
        history.Add("git commit");

        var match = history.FindMatch("gi");

        Assert.Equal("git commit", match);
    }

    [Fact]
    public void FindMatch_ReturnsNull_WhenNoMatch()
    {
        var history = new CommandHistory();
        history.Add("git add .");

        Assert.Null(history.FindMatch("xyz"));
    }

    [Fact]
    public void FindMatch_ReturnsNull_ForEmptyPrefix()
    {
        var history = new CommandHistory();
        history.Add("git add .");

        Assert.Null(history.FindMatch(""));
        Assert.Null(history.FindMatch("  "));
    }

    [Fact]
    public void FindMatch_IsCaseInsensitive()
    {
        var history = new CommandHistory();
        history.Add("Git Add .");

        Assert.Equal("Git Add .", history.FindMatch("git"));
    }

    [Fact]
    public void FindMatch_SkipsExactMatch()
    {
        var history = new CommandHistory();
        history.Add("git");

        Assert.Null(history.FindMatch("git"));
    }

    [Fact]
    public void Add_DeduplicatesMovesToEnd()
    {
        var history = new CommandHistory();
        history.Add("a");
        history.Add("b");
        history.Add("a");

        var all = history.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("b", all[0]);
        Assert.Equal("a", all[1]);
    }

    [Fact]
    public void Add_TrimsAt1000Entries()
    {
        var history = new CommandHistory();
        for (int i = 0; i < 1001; i++)
        {
            history.Add($"command-{i}");
        }

        var all = history.GetAll();
        Assert.Equal(1000, all.Count);
        Assert.Equal("command-1", all[0]);
        Assert.Equal("command-1000", all[999]);
    }

    [Fact]
    public void Add_IgnoresEmptyOrWhitespace()
    {
        var history = new CommandHistory();
        history.Add("");
        history.Add("  ");

        Assert.Empty(history.GetAll());
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var filePath = Path.Combine(tempDir, "history.json");

        var history1 = new CommandHistory(filePath);
        history1.Add("git add .");
        history1.Add("git commit -m 'test'");
        history1.Save();

        var history2 = new CommandHistory(filePath);
        history2.Load();

        var all = history2.GetAll();
        Assert.Equal(2, all.Count);
        Assert.Equal("git add .", all[0]);
        Assert.Equal("git commit -m 'test'", all[1]);
    }

    [Fact]
    public void Load_HandlesCorruptFile()
    {
        var filePath = Path.Combine(tempDir, "history.json");
        File.WriteAllText(filePath, "not valid json {{{");

        var history = new CommandHistory(filePath);
        history.Load(); // should not throw

        Assert.Empty(history.GetAll());
    }

    [Fact]
    public void Load_HandlesMissingFile()
    {
        var filePath = Path.Combine(tempDir, "nonexistent.json");

        var history = new CommandHistory(filePath);
        history.Load(); // should not throw

        Assert.Empty(history.GetAll());
    }

    [Fact]
    public void Add_DeduplicatesIsCaseInsensitive()
    {
        var history = new CommandHistory();
        history.Add("Git Add .");
        history.Add("git add .");

        var all = history.GetAll();
        Assert.Single(all);
        Assert.Equal("git add .", all[0]);
    }
}
