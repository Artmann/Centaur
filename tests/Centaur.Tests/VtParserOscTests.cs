using System.Text;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class VtParserOscTests
{
    readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    [Fact]
    public void Osc_SetTitle_ProducesNoVisibleOutput()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // ESC ] 0;title BEL
        parser.Process(Encoding.ASCII.GetBytes("\x1b]0;My Window Title\a"));

        Assert.Equal(' ', buffer[0, 0].character);
    }

    [Fact]
    public void Osc_SetTitle_WithStTerminator_ProducesNoVisibleOutput()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // ESC ] 0;title ESC backslash
        parser.Process(Encoding.ASCII.GetBytes("\x1b]0;title\x1b\\"));

        Assert.Equal(' ', buffer[0, 0].character);
    }

    [Fact]
    public void Osc_FollowedByText_OnlyTextVisible()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        parser.Process(Encoding.ASCII.GetBytes("\x1b]0;title\a" + "hello"));

        Assert.Equal('h', buffer[0, 0].character);
        Assert.Equal('e', buffer[1, 0].character);
        Assert.Equal('l', buffer[2, 0].character);
        Assert.Equal('l', buffer[3, 0].character);
        Assert.Equal('o', buffer[4, 0].character);
    }

    [Fact]
    public void Osc_MultipleInSequence_AllSilenced()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        parser.Process(Encoding.ASCII.GetBytes("\x1b]0;first\a\x1b]0;second\a" + "AB"));

        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[1, 0].character);
    }
}
