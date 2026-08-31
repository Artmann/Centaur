using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ConfigPathsTests
{
    [Fact]
    public void Root_WithoutOverride_IsCentaurUnderApplicationData()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Centaur"
        );

        Assert.Equal(expected, ConfigPaths.Root(_ => null));
    }

    [Fact]
    public void Root_WithOverride_UsesIt()
    {
        var root = ConfigPaths.Root(name =>
            name == "CENTAUR_CONFIG_DIR" ? @"C:\verify\config" : null
        );

        Assert.Equal(@"C:\verify\config", root);
    }

    // An empty or whitespace-only variable is what a caller that cleared it leaves behind;
    // treating it as an override would point the app at the working directory.
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Root_WithBlankOverride_FallsBackToApplicationData(string value)
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Centaur"
        );

        Assert.Equal(expected, ConfigPaths.Root(_ => value));
    }

    [Fact]
    public void For_JoinsTheFileNameOntoTheRoot()
    {
        var path = ConfigPaths.For(
            "session.json",
            name => name == "CENTAUR_CONFIG_DIR" ? @"C:\verify\config" : null
        );

        Assert.Equal(@"C:\verify\config\session.json", path);
    }
}
