using Centaur.App;
using Xunit;

namespace Centaur.Tests;

public class WorkingDirectoryTrackerTests : TempDirectory
{
    readonly string child;

    public WorkingDirectoryTrackerTests()
    {
        child = Path.Combine(TempDir, "child");
        Directory.CreateDirectory(child);
    }

    [Theory]
    [InlineData("cd ")]
    [InlineData("Set-Location ")]
    [InlineData("pushd ")]
    [InlineData("chdir ")]
    [InlineData("sl ")]
    public void Resolve_RecognizesEveryDirectoryChangePrefix(string prefix)
    {
        Assert.Equal(child, WorkingDirectoryTracker.Resolve(prefix + child, null));
    }

    [Fact]
    public void Resolve_PrefixIsCaseInsensitive()
    {
        Assert.Equal(child, WorkingDirectoryTracker.Resolve("CD " + child, null));
    }

    [Fact]
    public void Resolve_NonDirectoryCommand_ReturnsNull()
    {
        Assert.Null(WorkingDirectoryTracker.Resolve("git status", TempDir));
    }

    [Fact]
    public void Resolve_PrefixWithoutSeparator_IsNotADirectoryChange()
    {
        // "cdd" starts with the same letters but is a different command.
        Assert.Null(WorkingDirectoryTracker.Resolve("cdd " + child, null));
    }

    [Fact]
    public void Resolve_RelativeTarget_IsResolvedAgainstBaseDirectory()
    {
        Assert.Equal(child, WorkingDirectoryTracker.Resolve("cd child", TempDir));
    }

    [Fact]
    public void Resolve_DotDot_WalksUpFromBaseDirectory()
    {
        Assert.Equal(Path.GetFullPath(TempDir), WorkingDirectoryTracker.Resolve("cd ..", child));
    }

    [Fact]
    public void Resolve_RelativeTargetWithoutBaseDirectory_ReturnsNull()
    {
        Assert.Null(WorkingDirectoryTracker.Resolve("cd child", null));
    }

    [Fact]
    public void Resolve_StripsQuotesAroundTarget()
    {
        Assert.Equal(child, WorkingDirectoryTracker.Resolve("cd \"" + child + "\"", null));
        Assert.Equal(child, WorkingDirectoryTracker.Resolve("cd '" + child + "'", null));
    }

    [Fact]
    public void Resolve_TrimsSurroundingWhitespace()
    {
        Assert.Equal(child, WorkingDirectoryTracker.Resolve("cd   " + child + "  ", null));
    }

    [Fact]
    public void Resolve_Tilde_ExpandsToUserProfile()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        Assert.Equal(home, WorkingDirectoryTracker.Resolve("cd ~", null));
    }

    [Fact]
    public void Resolve_TildePath_ExpandsWithEitherSeparator()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var homeDirs = Directory.GetDirectories(home);
        Assert.NotEmpty(homeDirs);
        var name = Path.GetFileName(homeDirs[0]);

        var expected = Path.Combine(home, name);
        Assert.Equal(expected, WorkingDirectoryTracker.Resolve("cd ~/" + name, null));
        Assert.Equal(expected, WorkingDirectoryTracker.Resolve("cd ~\\" + name, null));
    }

    [Fact]
    public void Resolve_MissingDirectory_ReturnsNull()
    {
        Assert.Null(
            WorkingDirectoryTracker.Resolve("cd " + Path.Combine(TempDir, "nope"), TempDir)
        );
    }

    [Fact]
    public void Resolve_TargetIsAFile_ReturnsNull()
    {
        var file = TempFile("notadir.txt");
        File.WriteAllText(file, "x");

        Assert.Null(WorkingDirectoryTracker.Resolve("cd " + file, TempDir));
    }
}
