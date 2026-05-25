using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Bracketed paste encoding and paste safety.
///
/// Ported from ghostty/src/terminal/c/paste.zig: tests "encode bracketed",
/// "encode unbracketed no newlines", "encode unbracketed newlines",
/// "encode strip unsafe bytes", "is_safe with newline",
/// "is_safe with bracketed paste end", "is_safe with empty data".
///
/// When bracketed-paste mode (DEC 2004) is on, pasted text is wrapped in
/// ESC[200~ ... ESC[201~ and control bytes are neutralized to spaces. When
/// off, newlines are converted to carriage returns. IsSafe flags input that
/// could execute on paste (contains a newline, or smuggles the ESC[201~ end
/// marker).
///
/// Intended API (not yet implemented):
///   static class PasteEncoder {
///       static string Encode(string data, bool bracketed);
///       static bool IsSafe(string data);
///   }
/// </summary>
public class VtParserBracketedPasteTests
{
    [Fact]
    public void Encode_Bracketed_WrapsInMarkers()
    {
        var result = PasteEncoder.Encode("hello", bracketed: true);
        Assert.Equal("\x1b[200~hello\x1b[201~", result);
    }

    [Fact]
    public void Encode_Unbracketed_NoNewlines_PassesThrough()
    {
        var result = PasteEncoder.Encode("hello", bracketed: false);
        Assert.Equal("hello", result);
    }

    [Fact]
    public void Encode_Unbracketed_NewlinesBecomeCarriageReturns()
    {
        var result = PasteEncoder.Encode("hello\nworld", bracketed: false);
        Assert.Equal("hello\rworld", result);
    }

    [Fact]
    public void Encode_Bracketed_StripsUnsafeBytesToSpaces()
    {
        // ESC and NUL inside the payload are replaced with spaces so they
        // cannot terminate the paste or inject control sequences.
        var result = PasteEncoder.Encode("hel\x1blo\x00world", bracketed: true);
        Assert.Equal("\x1b[200~hel lo world\x1b[201~", result);
    }

    [Fact]
    public void IsSafe_PlainText_True()
    {
        Assert.True(PasteEncoder.IsSafe("hello world"));
    }

    [Fact]
    public void IsSafe_Newline_False()
    {
        Assert.False(PasteEncoder.IsSafe("hello\nworld"));
    }

    [Fact]
    public void IsSafe_EmbeddedEndMarker_False()
    {
        Assert.False(PasteEncoder.IsSafe("hello\x1b[201~world"));
    }

    [Fact]
    public void IsSafe_EmptyData_True()
    {
        Assert.True(PasteEncoder.IsSafe(""));
    }
}
