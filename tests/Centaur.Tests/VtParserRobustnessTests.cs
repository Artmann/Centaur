using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Parser robustness against malformed / adversarial sequences.
///
/// Ported from ghostty/src/terminal/Parser.zig ("csi: too many params"),
/// stream.zig ("stream: csi param too long"), and sgr.zig
/// ("sgr: direct color fg/bg missing color", "sgr: underline colon with
/// trailing separator and short slice" — both annotated "This used to crash").
///
/// These assert only that the parser recovers and keeps printing — they use
/// the existing API and should pass today, locking in that future SGR-style
/// work doesn't regress crash-safety.
/// </summary>
public class VtParserRobustnessTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;

    public VtParserRobustnessTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void Sgr_TooManyParams_RecoversAndPrints()
    {
        parser.Send("\x1b[" + string.Concat(Enumerable.Repeat("1;", 200)) + "mX");
        Assert.Equal('X', buffer[0, 0].character);
    }

    [Fact]
    public void Csi_ParamTooLong_RecoversAndPrints()
    {
        parser.Send("\x1b[11111111111111111111mX");
        Assert.Equal('X', buffer[0, 0].character);
    }

    [Fact]
    public void Sgr_ColonAndSemicolonMixed_RecoversAndPrints()
    {
        parser.Send("\x1b[38:2:0:1:2:3;1mX");
        Assert.Equal('X', buffer[0, 0].character);
    }

    [Fact]
    public void Sgr_DirectColorFgMissingColor_DoesNotCrash()
    {
        parser.Send("\x1b[38;5mX");
        Assert.Equal('X', buffer[0, 0].character);
    }

    [Fact]
    public void Sgr_DirectColorBgMissingColor_DoesNotCrash()
    {
        parser.Send("\x1b[48;5mX");
        Assert.Equal('X', buffer[0, 0].character);
    }

    [Fact]
    public void Sgr_UnderlineColonTrailingSeparator_DoesNotCrash()
    {
        // "ESC[58:4:m" historically tripped an assertion in Ghostty.
        parser.Send("\x1b[58:4:mX");
        Assert.Equal('X', buffer[0, 0].character);
    }
}
