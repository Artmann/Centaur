using System.Text;

namespace Centaur.Core.Terminal;

/// <summary>
/// Reassembles UTF-8 multi-byte sequences arriving one byte at a time from the PTY, which
/// splits them across reads freely. Holds only the partial sequence; a byte that is not part
/// of one is handed back to the caller to print as ASCII.
/// </summary>
sealed class Utf8Decoder
{
    readonly byte[] pending = new byte[4];

    // A surrogate pair is the widest a single codepoint decodes to.
    readonly char[] decoded = new char[2];

    int length;
    int remaining;

    /// <summary>
    /// Feeds one byte. Returns true when the byte belonged to a multi-byte sequence, in which
    /// case <paramref name="text"/> holds the decoded characters if that sequence just
    /// completed and is empty while it is still being filled. Returns false for a byte that is
    /// not part of one, leaving it to the caller.
    /// </summary>
    public bool TryDecode(byte b, out ReadOnlySpan<char> text)
    {
        text = default;

        if (remaining > 0 && (b & 0xC0) == 0x80)
        {
            pending[length++] = b;
            remaining--;
            if (remaining == 0)
            {
                text = decoded.AsSpan(
                    0,
                    Encoding.UTF8.GetChars(pending.AsSpan(0, length), decoded)
                );
            }
            return true;
        }

        var continuationBytes = ContinuationBytes(b);
        if (continuationBytes == 0)
        {
            return false;
        }

        pending[0] = b;
        length = 1;
        remaining = continuationBytes;
        return true;
    }

    /// <summary>How many continuation bytes follow this leading byte, or 0 if it is not one.</summary>
    static int ContinuationBytes(byte b) =>
        (b & 0xE0) == 0xC0 ? 1
        : (b & 0xF0) == 0xE0 ? 2
        : (b & 0xF8) == 0xF0 ? 3
        : 0;
}
