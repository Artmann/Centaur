using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// OSC 52 — clipboard get/set.
///
/// Ported from ghostty/src/terminal/osc/parsers/clipboard_operation.zig
/// (tests "OSC 52: get/set clipboard", "...(optional parameter)",
/// "OSC 52: clear clipboard"). Format is "52;{selection};{base64-or-?}".
/// The selection defaults to 'c' (clipboard) when omitted; data "?" is a
/// read request, other data is a write, empty data clears.
///
/// Intended API (not yet implemented):
///   record ClipboardRequest(char Selection, string Data);
///   VtParser: event Action&lt;ClipboardRequest&gt;? ClipboardChanged;  // writes/clears
///   A read request ("?") emits a reply on the Respond channel.
/// </summary>
public class VtParserOscClipboardTests
{
    readonly VtParser parser;

    public VtParserOscClipboardTests()
    {
        var theme = CatppuccinThemes.Macchiato;
        parser = new VtParser(new ScreenBuffer(80, 24, theme), theme);
    }

    [Fact]
    public void Osc52_SetClipboard_FiresWithSelectionAndData()
    {
        ClipboardRequest? captured = null;
        parser.ClipboardChanged += req => captured = req;

        // "AAAA" is base64; selection 's' (primary selection).
        parser.Send("\x1b]52;s;AAAA\a");

        Assert.NotNull(captured);
        Assert.Equal('s', captured!.Selection);
        Assert.Equal("AAAA", captured.Data);
    }

    [Fact]
    public void Osc52_DefaultSelection_IsClipboard()
    {
        ClipboardRequest? captured = null;
        parser.ClipboardChanged += req => captured = req;

        // Empty selection field defaults to 'c'.
        parser.Send("\x1b]52;;AAAA\a");

        Assert.NotNull(captured);
        Assert.Equal('c', captured!.Selection);
        Assert.Equal("AAAA", captured.Data);
    }

    [Fact]
    public void Osc52_Clear_FiresWithEmptyData()
    {
        ClipboardRequest? captured = null;
        parser.ClipboardChanged += req => captured = req;

        parser.Send("\x1b]52;;\a");

        Assert.NotNull(captured);
        Assert.Equal('c', captured!.Selection);
        Assert.Equal("", captured.Data);
    }

    [Fact]
    public void Osc52_ReadRequest_EmitsResponse()
    {
        var responses = TerminalTestHelpers.CaptureResponses(parser);

        // "?" requests the current clipboard contents; the terminal replies.
        parser.Send("\x1b]52;c;?\a");

        Assert.NotEmpty(responses);
    }
}
