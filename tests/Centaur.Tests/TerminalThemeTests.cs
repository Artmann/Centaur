using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class TerminalThemeTests
{
    [Fact]
    public void Macchiato_HasCorrectForeground()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFFCAD3F5u, theme.Foreground);
    }

    [Fact]
    public void Macchiato_HasCorrectBackground()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFF24273Au, theme.Background);
    }

    [Fact]
    public void Macchiato_HasCorrectCursor()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFFF4DBD6u, theme.Cursor); // Rosewater
    }

    [Fact]
    public void Macchiato_Palette_Has16Entries()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(16, theme.Palette.Length);
    }

    [Fact]
    public void Macchiato_Palette_AnsiRed()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFFED8796u, theme.Palette[1]); // Red
    }

    [Fact]
    public void Macchiato_Palette_AnsiGreen()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFFA6DA95u, theme.Palette[2]); // Green
    }

    [Fact]
    public void Macchiato_Palette_AnsiBlue()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(0xFF8AADF4u, theme.Palette[4]); // Blue
    }

    [Fact]
    public void GetColor_ReturnsBase16ForIndices0To15()
    {
        var theme = CatppuccinThemes.Macchiato;
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(theme.Palette[i], theme.GetColor(i));
        }
    }

    [Fact]
    public void FullPalette_Has256Entries()
    {
        var theme = CatppuccinThemes.Macchiato;
        Assert.Equal(256, theme.FullPalette.Length);
    }

    [Fact]
    public void FullPalette_First16MatchBase16()
    {
        var theme = CatppuccinThemes.Macchiato;
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(theme.Palette[i], theme.FullPalette[i]);
        }
    }

    [Fact]
    public void AllFourFlavorsAreDifferent()
    {
        var latte = CatppuccinThemes.Latte;
        var frappe = CatppuccinThemes.Frappe;
        var macchiato = CatppuccinThemes.Macchiato;
        var mocha = CatppuccinThemes.Mocha;

        Assert.NotEqual(latte.Background, frappe.Background);
        Assert.NotEqual(frappe.Background, macchiato.Background);
        Assert.NotEqual(macchiato.Background, mocha.Background);
    }

    [Fact]
    public void Latte_IsLightTheme()
    {
        var theme = CatppuccinThemes.Latte;
        Assert.Equal(0xFFEFF1F5u, theme.Background); // Light base
        Assert.Equal(0xFF4C4F69u, theme.Foreground); // Dark text
    }

    [Fact]
    public void Mocha_HasCorrectBackground()
    {
        var theme = CatppuccinThemes.Mocha;
        Assert.Equal(0xFF1E1E2Eu, theme.Background);
    }

    [Fact]
    public void Frappe_HasCorrectBackground()
    {
        var theme = CatppuccinThemes.Frappe;
        Assert.Equal(0xFF303446u, theme.Background);
    }
}

public class PaletteGeneratorTests
{
    [Fact]
    public void GenerateFullPalette_Returns256Colors()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );
        Assert.Equal(256, palette.Length);
    }

    [Fact]
    public void GenerateFullPalette_First16AreBase16()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );
        for (int i = 0; i < 16; i++)
        {
            Assert.Equal(theme.Palette[i], palette[i]);
        }
    }

    [Fact]
    public void GrayscaleRamp_FirstEntryNearBackground()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );

        // Index 232 should be close to background
        var rampStart = palette[232];
        AssertColorsClose(theme.Background, rampStart, tolerance: 30);
    }

    [Fact]
    public void GrayscaleRamp_LastEntryNearForeground()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );

        // Index 255 should be close to foreground
        var rampEnd = palette[255];
        AssertColorsClose(theme.Foreground, rampEnd, tolerance: 30);
    }

    [Fact]
    public void ColorCube_Origin_NearBackground()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );

        // Index 16 = cube(0,0,0) should be near background
        AssertColorsClose(theme.Background, palette[16], tolerance: 30);
    }

    [Fact]
    public void ColorCube_Max_NearForeground()
    {
        var theme = CatppuccinThemes.Macchiato;
        var palette = PaletteGenerator.GenerateFullPalette(
            theme.Palette,
            theme.Background,
            theme.Foreground
        );

        // Index 231 = cube(5,5,5) should be near foreground
        AssertColorsClose(theme.Foreground, palette[231], tolerance: 30);
    }

    [Fact]
    public void RgbToLab_AndBack_RoundTrips()
    {
        uint color = 0xFFED8796; // Macchiato red
        var lab = PaletteGenerator.RgbToLab(color);
        var back = PaletteGenerator.LabToRgb(lab);

        AssertColorsClose(color, back, tolerance: 2);
    }

    static void AssertColorsClose(uint expected, uint actual, int tolerance)
    {
        var er = (byte)(expected >> 16);
        var eg = (byte)(expected >> 8);
        var eb = (byte)expected;
        var ar = (byte)(actual >> 16);
        var ag = (byte)(actual >> 8);
        var ab = (byte)actual;

        Assert.True(Math.Abs(er - ar) <= tolerance, $"Red channel: expected {er}, got {ar}");
        Assert.True(Math.Abs(eg - ag) <= tolerance, $"Green channel: expected {eg}, got {ag}");
        Assert.True(Math.Abs(eb - ab) <= tolerance, $"Blue channel: expected {eb}, got {ab}");
    }
}
