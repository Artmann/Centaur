using System.Runtime.InteropServices;

namespace Centaur.Core.Terminal;

/// <summary>What feeding one byte completed, if anything.</summary>
enum VtToken
{
    /// <summary>The byte went into a sequence that is still being read.</summary>
    None,

    /// <summary>Printable text, in <see cref="VtTokenizer.Text"/>.</summary>
    Print,

    /// <summary>A C0 control byte, in <see cref="VtTokenizer.Code"/>.</summary>
    Control,

    /// <summary>A two-byte escape, its final byte in <see cref="VtTokenizer.Code"/>.</summary>
    Escape,

    /// <summary>A CSI sequence: <see cref="VtTokenizer.Csi"/> holds the parameters and
    /// <see cref="VtTokenizer.Code"/> the final byte.</summary>
    Csi,

    /// <summary>An OSC payload, in <see cref="VtTokenizer.OscPayload"/>.</summary>
    Osc,
}

/// <summary>
/// The byte-level half of VT parsing: the state machine that finds where each sequence starts
/// and ends in the pty's byte stream, including reassembling UTF-8 that arrives split across
/// reads. It decides nothing about what a sequence means and never touches the screen -
/// <see cref="VtParser"/> feeds it one byte at a time and acts on the token that comes back.
/// </summary>
sealed class VtTokenizer
{
    enum State
    {
        Ground,
        Escape,
        Csi,
        Osc,
        OscEscape,
    }

    State state = State.Ground;
    readonly Utf8Decoder utf8 = new();

    // OSC payload accumulator (bytes between ESC] and the terminator).
    readonly List<byte> oscBuffer = new();

    // A surrogate pair is the widest a single codepoint decodes to.
    readonly char[] printed = new char[2];
    int printedLength;

    /// <summary>Parameters of the CSI sequence the last <see cref="VtToken.Csi"/> completed.</summary>
    public CsiSequence Csi { get; } = new();

    /// <summary>The control byte, or the final byte of the escape or CSI sequence.</summary>
    public byte Code { get; private set; }

    /// <summary>Text the last <see cref="VtToken.Print"/> decoded.</summary>
    public ReadOnlySpan<char> Text => printed.AsSpan(0, printedLength);

    /// <summary>Payload of the last <see cref="VtToken.Osc"/>: the bytes between ESC] and
    /// whichever terminator ended it.</summary>
    public ReadOnlySpan<byte> OscPayload => CollectionsMarshal.AsSpan(oscBuffer);

    public VtToken Feed(byte b) =>
        state switch
        {
            State.Escape => AfterEscape(b),
            State.Csi => InCsi(b),
            State.Osc => InOsc(b),
            State.OscEscape => AfterOscEscape(b),
            _ => InGround(b),
        };

    VtToken InGround(byte b)
    {
        if (b == 0x1B) // ESC
        {
            state = State.Escape;
            return VtToken.None;
        }
        if (b < 0x20)
        {
            Code = b;
            return VtToken.Control;
        }

        // The decoder claims the bytes of a multi-byte sequence and yields nothing until that
        // sequence completes. Anything it rejects prints as-is, DEL included.
        if (utf8.TryDecode(b, out var text))
        {
            return Print(text);
        }

        printed[0] = (char)b;
        printedLength = 1;
        return VtToken.Print;
    }

    VtToken AfterEscape(byte b)
    {
        // Every escape ends here unless it opens a longer sequence.
        state = State.Ground;
        switch (b)
        {
            case (byte)'[': // CSI
                state = State.Csi;
                Csi.Begin();
                return VtToken.None;
            case (byte)']': // OSC - Operating System Command
                state = State.Osc;
                oscBuffer.Clear();
                return VtToken.None;
            default:
                Code = b;
                return VtToken.Escape;
        }
    }

    VtToken InCsi(byte b)
    {
        if (Csi.TryAccumulate(b))
        {
            return VtToken.None;
        }

        // Either the sequence ends on this byte or the byte was junk; both end it.
        state = State.Ground;
        if (b < 0x40 || b > 0x7E)
        {
            return VtToken.None;
        }

        Csi.Push();
        Code = b;
        return VtToken.Csi;
    }

    VtToken InOsc(byte b)
    {
        if (b == 0x07) // BEL terminates OSC
        {
            state = State.Ground;
            return VtToken.Osc;
        }
        if (b == 0x1B) // Could be the start of ST (ESC backslash)
        {
            state = State.OscEscape;
            return VtToken.None;
        }

        oscBuffer.Add(b);
        return VtToken.None;
    }

    VtToken AfterOscEscape(byte b)
    {
        // ST terminates the OSC; any other byte just ends it.
        state = State.Ground;
        return b == (byte)'\\' ? VtToken.Osc : VtToken.None;
    }

    VtToken Print(ReadOnlySpan<char> text)
    {
        text.CopyTo(printed);
        printedLength = text.Length;
        return printedLength > 0 ? VtToken.Print : VtToken.None;
    }
}
