using Centaur.Core.Terminal;

namespace Centaur.Core.Hosting;

public interface IThemeProvider : IProvider
{
    IReadOnlyList<ThemeInfo> GetThemes();
}

public record ThemeInfo(string Id, string DisplayName, string Group, TerminalTheme Theme);
