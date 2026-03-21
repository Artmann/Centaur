using Centaur.Core.Hosting;

namespace Centaur.Core.Terminal;

public class CatppuccinThemeProvider : IThemeProvider
{
    public IReadOnlyList<ThemeInfo> GetThemes() =>
    [
        new("catppuccin-latte", "Latte", "Catppuccin", CatppuccinThemes.Latte),
        new("catppuccin-frappe", "Frappe", "Catppuccin", CatppuccinThemes.Frappe),
        new("catppuccin-macchiato", "Macchiato", "Catppuccin", CatppuccinThemes.Macchiato),
        new("catppuccin-mocha", "Mocha", "Catppuccin", CatppuccinThemes.Mocha),
    ];
}
