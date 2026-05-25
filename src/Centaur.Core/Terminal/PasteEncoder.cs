using System.Text;

namespace Centaur.Core.Terminal;

/// <summary>
/// Prepares clipboard text for injection into the PTY. In bracketed-paste mode
/// (DEC 2004) the text is wrapped in ESC[200~ … ESC[201~ and control bytes are
/// neutralized to spaces so the payload cannot terminate the paste or inject
/// escape sequences. Unbracketed, newlines are converted to carriage returns.
/// </summary>
public static class PasteEncoder
{
    const string Start = "\x1b[200~";
    const string End = "\x1b[201~";

    public static string Encode(string data, bool bracketed)
    {
        if (!bracketed)
        {
            // Terminals expect Enter as CR, not LF, on the input stream.
            return data.Replace('\n', '\r');
        }

        var sb = new StringBuilder(data.Length + Start.Length + End.Length);
        sb.Append(Start);
        foreach (var c in data)
        {
            sb.Append(IsUnsafe(c) ? ' ' : c);
        }
        sb.Append(End);
        return sb.ToString();
    }

    /// <summary>
    /// True when the data can be pasted without risk of executing: it contains
    /// no newline (which would submit a command) and does not smuggle the
    /// bracketed-paste end marker.
    /// </summary>
    public static bool IsSafe(string data) =>
        !data.Contains('\n') && !data.Contains(End, StringComparison.Ordinal);

    // A C0 control (or DEL) other than tab/newline/carriage-return is stripped.
    static bool IsUnsafe(char c) => (c < 0x20 && c != '\t' && c != '\n' && c != '\r') || c == 0x7F;
}
