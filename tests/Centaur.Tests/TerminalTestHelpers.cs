using System.Text;
using Centaur.Core.Terminal;

namespace Centaur.Tests;

/// <summary>
/// Shared helpers for the ported Ghostty terminal-emulation tests.
///
/// These tests were translated from Ghostty's Zig test suite
/// (C:\Users\Artga\Code\ghostty\src\terminal\*). Ghostty tests isolated
/// units (sgr.Parser, device_attributes.encode, the paste/mouse encoders);
/// Centaur is monolithic (VtParser -> ScreenBuffer), so each test is
/// re-expressed behaviorally against Centaur's surface. Several reference
/// an *intended* API that does not exist yet — these files intentionally
/// will not compile until the matching feature lands (TDD red phase).
/// </summary>
static class TerminalTestHelpers
{
    /// <summary>Feed a byte string into the parser, encoded as UTF-8 so that
    /// multi-byte characters (e.g. an em dash in a title) reach the parser as
    /// their real UTF-8 bytes. Control/ASCII bytes are unaffected.</summary>
    public static void Send(this VtParser parser, string text)
    {
        parser.Process(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>Subscribe to the parser's PTY-bound response channel and
    /// collect each emitted reply as a Latin1 string. Used by DA/DECRQM and
    /// OSC query tests that must reply to the host.</summary>
    public static List<string> CaptureResponses(VtParser parser)
    {
        var responses = new List<string>();
        parser.Respond += bytes => responses.Add(Encoding.Latin1.GetString(bytes));
        return responses;
    }
}
