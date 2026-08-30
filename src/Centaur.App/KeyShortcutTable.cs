using Avalonia.Input;

namespace Centaur.App;

/// <summary>
/// An ordered list of keyboard shortcuts, tried in registration order.
///
/// A handler returns false to decline the key - Ctrl+C only copies when there is a
/// selection, Shift+PageUp only scrolls off the alternate screen - and the search moves on
/// to the next entry, then to the caller's fallback. That is what keeps the conditional
/// shortcuts from swallowing the byte the shell was supposed to receive.
/// </summary>
public sealed class KeyShortcutTable
{
    readonly List<(Key Key, KeyModifiers Modifiers, Func<bool> Handle)> entries = [];

    /// <param name="modifiers">
    /// Modifiers that must be held. Extra ones are allowed, so Ctrl+C also matches
    /// Ctrl+Shift+C; <see cref="KeyModifiers.None"/> instead demands a bare key press.
    /// </param>
    /// <param name="handle">Returns true if it consumed the key, false to decline it.</param>
    public KeyShortcutTable Add(Key key, KeyModifiers modifiers, Func<bool> handle)
    {
        entries.Add((key, modifiers, handle));
        return this;
    }

    /// <summary>Registers a shortcut that always consumes its key.</summary>
    public KeyShortcutTable Add(Key key, KeyModifiers modifiers, Action handle)
    {
        return Add(
            key,
            modifiers,
            () =>
            {
                handle();
                return true;
            }
        );
    }

    /// <summary>Runs the first matching shortcut that accepts the key.</summary>
    public bool TryHandle(Key key, KeyModifiers modifiers)
    {
        foreach (var entry in entries)
        {
            if (entry.Key == key && Matches(entry.Modifiers, modifiers) && entry.Handle())
            {
                return true;
            }
        }

        return false;
    }

    static bool Matches(KeyModifiers required, KeyModifiers pressed)
    {
        if (required == KeyModifiers.None)
        {
            return pressed == KeyModifiers.None;
        }

        return (pressed & required) == required;
    }
}
