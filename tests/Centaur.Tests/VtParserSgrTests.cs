using System.Text;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class VtParserSgrTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserSgrTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24);
        parser = new VtParser(buffer, theme);
    }

    void Send(string text)
    {
        parser.Process(Encoding.ASCII.GetBytes(text));
    }

    [Fact]
    public void DefaultColors_AreThemeForegroundBackground()
    {
        Send("A");
        Assert.Equal(theme.Foreground, buffer[0, 0].foreground);
        Assert.Equal(theme.Background, buffer[0, 0].background);
    }

    [Fact]
    public void Sgr_Reset_RestoresToThemeDefaults()
    {
        Send("\x1b[31mX\x1b[0mY");
        // Y should have default colors
        Assert.Equal(theme.Foreground, buffer[1, 0].foreground);
        Assert.Equal(theme.Background, buffer[1, 0].background);
    }

    [Fact]
    public void Sgr_StandardForeground_Red()
    {
        Send("\x1b[31mR");
        Assert.Equal(theme.Palette[1], buffer[0, 0].foreground); // Red
    }

    [Fact]
    public void Sgr_StandardForeground_Green()
    {
        Send("\x1b[32mG");
        Assert.Equal(theme.Palette[2], buffer[0, 0].foreground); // Green
    }

    [Fact]
    public void Sgr_StandardBackground_Blue()
    {
        Send("\x1b[44mB");
        Assert.Equal(theme.Palette[4], buffer[0, 0].background); // Blue
    }

    [Fact]
    public void Sgr_DefaultForeground_Code39()
    {
        Send("\x1b[31m\x1b[39mA");
        Assert.Equal(theme.Foreground, buffer[0, 0].foreground);
    }

    [Fact]
    public void Sgr_DefaultBackground_Code49()
    {
        Send("\x1b[41m\x1b[49mA");
        Assert.Equal(theme.Background, buffer[0, 0].background);
    }

    [Fact]
    public void Sgr_BrightForeground()
    {
        Send("\x1b[91mA");
        Assert.Equal(theme.Palette[9], buffer[0, 0].foreground); // Bright Red
    }

    [Fact]
    public void Sgr_BrightBackground()
    {
        Send("\x1b[102mA");
        Assert.Equal(theme.Palette[10], buffer[0, 0].background); // Bright Green
    }

    [Fact]
    public void Sgr_256Color_Foreground_PaletteIndex()
    {
        // 38;5;4 = Blue from palette
        Send("\x1b[38;5;4mA");
        Assert.Equal(theme.GetColor(4), buffer[0, 0].foreground);
    }

    [Fact]
    public void Sgr_256Color_Background_PaletteIndex()
    {
        Send("\x1b[48;5;2mA");
        Assert.Equal(theme.GetColor(2), buffer[0, 0].background);
    }

    [Fact]
    public void Sgr_256Color_ExtendedIndex()
    {
        // 38;5;196 should be a color from the generated cube
        Send("\x1b[38;5;196mA");
        Assert.Equal(theme.GetColor(196), buffer[0, 0].foreground);
    }

    [Fact]
    public void Sgr_TrueColor_Foreground()
    {
        // 38;2;255;128;0 = orange
        Send("\x1b[38;2;255;128;0mA");
        Assert.Equal(0xFFFF8000u, buffer[0, 0].foreground);
    }

    [Fact]
    public void Sgr_TrueColor_Background()
    {
        Send("\x1b[48;2;0;255;0mA");
        Assert.Equal(0xFF00FF00u, buffer[0, 0].background);
    }

    [Fact]
    public void Sgr_CombinedParams_FgAndBg()
    {
        // Set both red fg and blue bg in one sequence
        Send("\x1b[31;44mA");
        Assert.Equal(theme.Palette[1], buffer[0, 0].foreground);
        Assert.Equal(theme.Palette[4], buffer[0, 0].background);
    }

    [Fact]
    public void Sgr_NoParams_IsReset()
    {
        // ESC[m with no params should reset (same as ESC[0m)
        Send("\x1b[31mX\x1b[mY");
        Assert.Equal(theme.Foreground, buffer[1, 0].foreground);
    }

    [Fact]
    public void Sgr_ColorPersistsAcrossCharacters()
    {
        Send("\x1b[32mABC");
        Assert.Equal(theme.Palette[2], buffer[0, 0].foreground);
        Assert.Equal(theme.Palette[2], buffer[1, 0].foreground);
        Assert.Equal(theme.Palette[2], buffer[2, 0].foreground);
    }

    [Fact]
    public void Sgr_AllStandardForegroundColors()
    {
        for (int i = 0; i < 8; i++)
        {
            var buf = new ScreenBuffer(80, 24);
            var p = new VtParser(buf, theme);
            p.Process(Encoding.ASCII.GetBytes($"\x1b[{30 + i}mX"));
            Assert.Equal(theme.Palette[i], buf[0, 0].foreground);
        }
    }

    [Fact]
    public void Sgr_AllStandardBackgroundColors()
    {
        for (int i = 0; i < 8; i++)
        {
            var buf = new ScreenBuffer(80, 24);
            var p = new VtParser(buf, theme);
            p.Process(Encoding.ASCII.GetBytes($"\x1b[{40 + i}mX"));
            Assert.Equal(theme.Palette[i], buf[0, 0].background);
        }
    }
}
