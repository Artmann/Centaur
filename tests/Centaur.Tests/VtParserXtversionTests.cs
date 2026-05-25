using System.Reflection;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// XTVERSION — CSI &gt; q — asks the terminal to identify itself. Claude Code emits this
/// at startup (CSI &gt; 0 q) and blocks on a ~5s timeout if it goes unanswered, stalling
/// before it will echo input. The terminal must reply with a DCS &gt; | name version ST
/// string on the Respond channel.
/// </summary>
public class VtParserXtversionTests
{
    readonly VtParser parser;
    readonly List<string> responses;

    // The reported version is tied to the assembly build version (set in
    // Directory.Build.props) rather than a hardcoded literal, so it can't drift.
    // Derive the same value here instead of duplicating the version number.
    static readonly string expectedReply = $"\x1bP>|Centaur({ResolveVersion()})\x1b\\";

    static string ResolveVersion()
    {
        var info = typeof(VtParser)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            var plus = info.IndexOf('+', System.StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
        var version = typeof(VtParser).Assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    public VtParserXtversionTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
        responses = TerminalTestHelpers.CaptureResponses(parser);
    }

    [Fact]
    public void Xtversion_RepliesDcsVersionString()
    {
        // CSI > 0 q -> DCS > | Centaur(version) ST.
        parser.Send("\x1b[>0q");
        Assert.Equal(expectedReply, Assert.Single(responses));
    }

    [Fact]
    public void Xtversion_NoParam_StillReplies()
    {
        parser.Send("\x1b[>q");
        Assert.Equal(expectedReply, Assert.Single(responses));
    }
}
