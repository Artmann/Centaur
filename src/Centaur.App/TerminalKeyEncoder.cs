using System.Collections.Frozen;
using Avalonia.Input;

namespace Centaur.App;

public static class TerminalKeyEncoder
{
    // One sequence per key, resolved by lookup rather than a switch ladder so adding a key
    // is a table entry. Modifier-dependent keys are handled in Encode, not here.
    static readonly FrozenDictionary<Key, byte[]> sequences = new Dictionary<Key, byte[]>
    {
        [Key.Enter] = "\r"u8.ToArray(),
        [Key.Back] = "\x7f"u8.ToArray(),
        [Key.Tab] = "\t"u8.ToArray(),
        [Key.Escape] = "\x1b"u8.ToArray(),
        [Key.Up] = "\x1b[A"u8.ToArray(),
        [Key.Down] = "\x1b[B"u8.ToArray(),
        [Key.Right] = "\x1b[C"u8.ToArray(),
        [Key.Left] = "\x1b[D"u8.ToArray(),
        [Key.Home] = "\x1b[H"u8.ToArray(),
        [Key.End] = "\x1b[F"u8.ToArray(),
        [Key.Insert] = "\x1b[2~"u8.ToArray(),
        [Key.Delete] = "\x1b[3~"u8.ToArray(),
        [Key.PageUp] = "\x1b[5~"u8.ToArray(),
        [Key.PageDown] = "\x1b[6~"u8.ToArray(),
        [Key.F1] = "\x1bOP"u8.ToArray(),
        [Key.F2] = "\x1bOQ"u8.ToArray(),
        [Key.F3] = "\x1bOR"u8.ToArray(),
        [Key.F4] = "\x1bOS"u8.ToArray(),
        [Key.F5] = "\x1b[15~"u8.ToArray(),
        [Key.F6] = "\x1b[17~"u8.ToArray(),
        [Key.F7] = "\x1b[18~"u8.ToArray(),
        [Key.F8] = "\x1b[19~"u8.ToArray(),
        [Key.F9] = "\x1b[20~"u8.ToArray(),
        [Key.F10] = "\x1b[21~"u8.ToArray(),
        [Key.F11] = "\x1b[23~"u8.ToArray(),
        [Key.F12] = "\x1b[24~"u8.ToArray(),
    }.ToFrozenDictionary();

    // DECCKM (mode 1). With application cursor keys on, the cursor keys switch from the CSI
    // form to SS3; a program that asked for them and gets CSI sees a different key entirely.
    // Only these six move - the rest of the table is mode-independent.
    static readonly FrozenDictionary<Key, byte[]> applicationCursorSequences = new Dictionary<
        Key,
        byte[]
    >
    {
        [Key.Up] = "\x1bOA"u8.ToArray(),
        [Key.Down] = "\x1bOB"u8.ToArray(),
        [Key.Right] = "\x1bOC"u8.ToArray(),
        [Key.Left] = "\x1bOD"u8.ToArray(),
        [Key.Home] = "\x1bOH"u8.ToArray(),
        [Key.End] = "\x1bOF"u8.ToArray(),
    }.ToFrozenDictionary();

    // Shift+Tab is the classic "backtab" sequence; plain Tab stays \t.
    static readonly byte[] backTab = "\x1b[Z"u8.ToArray();

    /// <summary>
    /// Returns the bytes a key press should put on the pty's input, or null when the key has
    /// no sequence of its own and the caller should fall back to the typed text.
    /// The array is a fresh copy, so callers may keep or mutate it.
    /// </summary>
    public static byte[]? Encode(
        Key key,
        KeyModifiers modifiers,
        bool applicationCursorKeys = false
    )
    {
        if (key == Key.Tab && modifiers.HasFlag(KeyModifiers.Shift))
        {
            return backTab.ToArray();
        }

        if (applicationCursorKeys && applicationCursorSequences.TryGetValue(key, out var applied))
        {
            return applied.ToArray();
        }

        return sequences.TryGetValue(key, out var sequence) ? sequence.ToArray() : null;
    }
}
