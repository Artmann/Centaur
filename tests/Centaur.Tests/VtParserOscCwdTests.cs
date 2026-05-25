using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 7 — report working directory.
///
/// Ported from ghostty/src/terminal/osc/parsers/report_pwd.zig
/// (tests "OSC 7: report pwd", "OSC 7: report pwd empty"). The payload is a
/// file:// URI; Centaur tracks it to power CWD-aware new tabs/splits.
///
/// Intended API (not yet implemented): VtParser exposes
/// string? WorkingDirectory, set from the OSC 7 payload.
/// </summary>
public class VtParserOscCwdTests
{
    readonly VtParser parser;

    public VtParserOscCwdTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
    }

    [Fact]
    public void Osc7_ReportsWorkingDirectory()
    {
        parser.Send("\x1b]7;file:///tmp/example\a");
        Assert.Equal("file:///tmp/example", parser.WorkingDirectory);
    }

    [Fact]
    public void Osc7_Empty_SetsEmptyWorkingDirectory()
    {
        parser.Send("\x1b]7;\a");
        Assert.Equal("", parser.WorkingDirectory);
    }
}
