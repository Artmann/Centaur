using System.Text;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class VtParserUtf8Tests
{
    readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    [Fact]
    public void Utf8_TwoByteChar_RendersCorrectly()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // © = U+00A9 = 0xC2 0xA9
        parser.Process(new byte[] { 0xC2, 0xA9 });

        Assert.Equal('©', buffer[0, 0].character);
    }

    [Fact]
    public void Utf8_ThreeByteChar_BoxDrawing_RendersCorrectly()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // ─ = U+2500 = 0xE2 0x94 0x80
        parser.Process(new byte[] { 0xE2, 0x94, 0x80 });

        Assert.Equal('─', buffer[0, 0].character);
    }

    [Fact]
    public void Utf8_MultipleBoxDrawingChars_RenderCorrectly()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // ┌─┐ = three 3-byte UTF-8 characters
        var bytes = Encoding.UTF8.GetBytes("┌─┐");
        parser.Process(bytes);

        Assert.Equal('┌', buffer[0, 0].character);
        Assert.Equal('─', buffer[1, 0].character);
        Assert.Equal('┐', buffer[2, 0].character);
    }

    [Fact]
    public void Utf8_MixedAsciiAndMultibyte_RenderCorrectly()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        var bytes = Encoding.UTF8.GetBytes("hi─ok");
        parser.Process(bytes);

        Assert.Equal('h', buffer[0, 0].character);
        Assert.Equal('i', buffer[1, 0].character);
        Assert.Equal('─', buffer[2, 0].character);
        Assert.Equal('o', buffer[3, 0].character);
        Assert.Equal('k', buffer[4, 0].character);
    }

    [Fact]
    public void Utf8_FourByteChar_DoesNotCrash()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // 😀 = U+1F600 = 0xF0 0x9F 0x98 0x80
        // This is outside BMP so char representation uses surrogate pairs
        // Just verify it doesn't crash
        parser.Process(new byte[] { 0xF0, 0x9F, 0x98, 0x80 });
    }

    [Fact]
    public void Utf8_SplitAcrossProcessCalls_RendersCorrectly()
    {
        var buffer = new ScreenBuffer(80, 24);
        var parser = new VtParser(buffer, theme);

        // ─ = 0xE2 0x94 0x80, split across two Process calls
        parser.Process(new byte[] { 0xE2 });
        parser.Process(new byte[] { 0x94, 0x80 });

        Assert.Equal('─', buffer[0, 0].character);
    }
}
