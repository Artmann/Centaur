using System.Text;
using Centaur.Core.Terminal;

namespace Centaur.Tests;

/// <summary>A fresh 80x24 buffer wired to a parser on the default theme — the arrangement
/// almost every VtParser test starts from.</summary>
public abstract class VtParserFixture
{
    private protected readonly ScreenBuffer buffer;
    private protected readonly VtParser parser;
    private protected readonly TerminalTheme theme;

    protected VtParserFixture()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    /// <summary>Feeds ASCII bytes straight at the parser. Tests needing real UTF-8 use the
    /// <see cref="TerminalTestHelpers.Send"/> extension instead.</summary>
    protected void Send(string text)
    {
        parser.Process(Encoding.ASCII.GetBytes(text));
    }
}
