using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ThemeProviderTests
{
    [Fact]
    public void CatppuccinThemeProvider_ReturnsFourThemes()
    {
        var provider = new CatppuccinThemeProvider();
        var themes = provider.GetThemes();
        Assert.Equal(4, themes.Count);
    }

    [Fact]
    public void CatppuccinThemeProvider_ContainsMacchiato()
    {
        var provider = new CatppuccinThemeProvider();
        var themes = provider.GetThemes();
        var macchiato = themes.FirstOrDefault(t => t.Id == "catppuccin-macchiato");

        Assert.NotNull(macchiato);
        Assert.Equal("Macchiato", macchiato.DisplayName);
        Assert.Equal("Catppuccin", macchiato.Group);
        Assert.Equal(CatppuccinThemes.Macchiato.Foreground, macchiato.Theme.Foreground);
    }

    [Fact]
    public void CatppuccinThemeProvider_AllThemesHaveUniqueIds()
    {
        var provider = new CatppuccinThemeProvider();
        var themes = provider.GetThemes();
        var ids = themes.Select(t => t.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
    }

    [Fact]
    public void CatppuccinThemeProvider_ResolvedViaExtensionHost()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new CatppuccinThemeProvider());

        var provider = host.GetProvider<IThemeProvider>();
        Assert.NotNull(provider);

        var themes = provider.GetThemes();
        Assert.Equal(4, themes.Count);
    }
}
