using System.Text;
using Avalonia;
using Avalonia.Input;
using Centaur.Core.Terminal;
using MouseButton = Centaur.Core.Terminal.MouseButton;

namespace Centaur.App;

/// <summary>Where a pointer event ends up.</summary>
public enum MouseOutcome
{
    /// <summary>The pane keeps it: text selection, or scrollback for the wheel.</summary>
    Local,

    /// <summary>The program gets it, as a mouse report.</summary>
    Report,

    /// <summary>The program gets it as cursor keys - the wheel on the alternate screen, where
    /// there is no scrollback to move and the program never asked for the mouse.</summary>
    ScrollKeys,

    /// <summary>Nobody gets it. A program owns the pointer but does not want this event.</summary>
    Ignore,
}

/// <summary>The terminal state a routing decision reads, taken as one snapshot so
/// <see cref="TerminalMouse.Route"/> depends on the state rather than on where to find it.</summary>
public readonly record struct MouseModes(
    MouseTrackingMode tracking,
    bool alternateScreen,
    bool altScroll
);

/// <summary>
/// The mouse half of a pane: whether a pointer event belongs to the program or to the pane,
/// and the bytes it turns into.
///
/// Split out of <see cref="TerminalControl"/> the way <see cref="TerminalInput"/> was - the
/// control keeps the Avalonia overrides and acts on the answer. <see cref="Route"/> is the
/// whole decision and takes no Avalonia types, so it is testable on its own.
/// </summary>
public sealed class TerminalMouse
{
    // One notch moves three lines, matching TerminalSurface.ScrollByWheel so the wheel feels
    // the same on both screens.
    const int linesPerNotch = 3;

    readonly TerminalSurface surface;
    readonly ShellChannel shell;

    public TerminalMouse(TerminalSurface surface, ShellChannel shell)
    {
        this.surface = surface;
        this.shell = shell;
    }

    /// <summary>
    /// Set by the last press that was reported to a program, and read by the control before it
    /// lets the context menu open: a right-click the program received must not also open our
    /// menu. Shift+right-click routes local, so it still reaches the menu.
    /// </summary>
    public bool SuppressContextMenu { get; private set; }

    /// <summary>
    /// The decision, in resolution order. Shift is the user's escape hatch and always wins, so
    /// text stays selectable inside a program that grabbed the pointer.
    /// </summary>
    public static MouseOutcome Route(
        MouseButton button,
        MouseAction action,
        bool shift,
        MouseModes modes
    )
    {
        if (shift)
        {
            return MouseOutcome.Local;
        }

        var wheel = button is MouseButton.ScrollUp or MouseButton.ScrollDown;

        if (modes.tracking != MouseTrackingMode.Off)
        {
            // The wheel is reported at every tracking level; everything else depends on what
            // the level asked for. An unwanted event is swallowed rather than made local:
            // starting a selection under a program that owns the pointer would draw a
            // highlight it knows nothing about.
            if (wheel || Wanted(button, action, modes.tracking))
            {
                return MouseOutcome.Report;
            }

            return MouseOutcome.Ignore;
        }

        if (wheel && modes.alternateScreen && modes.altScroll)
        {
            return MouseOutcome.ScrollKeys;
        }

        return MouseOutcome.Local;
    }

    static bool Wanted(MouseButton button, MouseAction action, MouseTrackingMode tracking) =>
        tracking switch
        {
            // X10 (mode 9) reports the press and nothing else - no releases, no motion.
            MouseTrackingMode.X10 => action == MouseAction.Press,
            MouseTrackingMode.Normal => action != MouseAction.Motion,
            MouseTrackingMode.ButtonEvent => action != MouseAction.Motion
                || button != MouseButton.None,
            _ => true,
        };

    /// <summary>A button going down, addressed by whichever is now pressed.</summary>
    public bool TryHandlePress(PointerPointProperties properties, KeyModifiers keys, Point at) =>
        TryHandlePointer(ButtonFor(properties), MouseAction.Press, keys, at);

    /// <summary>Pointer movement. A drag reports the button held; under any-event tracking the
    /// program wants bare movement too, which arrives as no button at all.</summary>
    public bool TryHandleMove(PointerPointProperties properties, KeyModifiers keys, Point at) =>
        TryHandlePointer(ButtonFor(properties), MouseAction.Motion, keys, at);

    /// <summary>A button coming up, addressed by the one that started the press - by release
    /// time the properties no longer show it.</summary>
    public bool TryHandleRelease(Avalonia.Input.MouseButton button, KeyModifiers keys, Point at) =>
        TryHandlePointer(ButtonFor(button), MouseAction.Release, keys, at);

    static MouseButton ButtonFor(PointerPointProperties properties)
    {
        if (properties.IsLeftButtonPressed)
        {
            return MouseButton.Left;
        }
        if (properties.IsMiddleButtonPressed)
        {
            return MouseButton.Middle;
        }
        if (properties.IsRightButtonPressed)
        {
            return MouseButton.Right;
        }
        return MouseButton.None;
    }

    static MouseButton ButtonFor(Avalonia.Input.MouseButton button) =>
        button switch
        {
            Avalonia.Input.MouseButton.Left => MouseButton.Left,
            Avalonia.Input.MouseButton.Middle => MouseButton.Middle,
            Avalonia.Input.MouseButton.Right => MouseButton.Right,
            _ => MouseButton.None,
        };

    /// <summary>
    /// Returns true when the pane should keep its hands off - either the program got the
    /// event, or a program owning the pointer had it swallowed.
    /// </summary>
    bool TryHandlePointer(MouseButton button, MouseAction action, KeyModifiers keys, Point position)
    {
        var modes = surface.Parser.Modes;
        var outcome = Route(button, action, keys.HasFlag(KeyModifiers.Shift), Snapshot());

        // Only the press decides, and the answer has to survive until the release: Avalonia
        // raises ContextRequested after the button comes up, so recomputing it there would
        // always clear the flag and let the menu open over the program that took the click.
        if (action == MouseAction.Press)
        {
            SuppressContextMenu = button == MouseButton.Right && outcome != MouseOutcome.Local;
        }

        if (outcome != MouseOutcome.Report)
        {
            return outcome == MouseOutcome.Ignore;
        }

        SendReport(button, action, keys, position, modes);
        return true;
    }

    /// <summary>
    /// Handles a wheel notch. <paramref name="notches"/> is positive upward, matching Avalonia's
    /// delta. Returns true when the pane should not also scroll its own scrollback.
    /// </summary>
    public bool TryHandleWheel(int notches, KeyModifiers keys, Point position)
    {
        // A precise trackpad can report less than a whole notch, which truncates to zero. That
        // has no direction, so it is left to the scrollback path rather than guessed at - a
        // wrong-way wheel report is worse than a line of scrollback the user did not ask for.
        if (notches == 0)
        {
            return false;
        }

        var button = notches > 0 ? MouseButton.ScrollUp : MouseButton.ScrollDown;
        var modes = surface.Parser.Modes;
        var outcome = Route(
            button,
            MouseAction.Press,
            keys.HasFlag(KeyModifiers.Shift),
            Snapshot()
        );
        var repeats = Math.Max(1, Math.Abs(notches));

        switch (outcome)
        {
            case MouseOutcome.Report:
                for (var i = 0; i < repeats; i++)
                {
                    SendReport(button, MouseAction.Press, keys, position, modes);
                }
                return true;

            case MouseOutcome.ScrollKeys:
                SendScrollKeys(notches > 0, repeats, modes);
                return true;

            case MouseOutcome.Ignore:
                return true;

            default:
                return false;
        }
    }

    void SendReport(
        MouseButton button,
        MouseAction action,
        KeyModifiers keys,
        Point position,
        DecModes modes
    )
    {
        var (col, row) = surface.CellAt(position);
        var modifiers = Modifiers(keys, modes.MouseTracking);
        var report = modes.MouseSgrMode
            ? MouseReport.EncodeSgr(button, col, row, action, modifiers)
            : MouseReport.EncodeX10(button, col, row, action, modifiers);

        // Latin-1, not UTF-8: an X10 coordinate runs to 255 and has to stay one byte. SGR is
        // all ASCII, so the encoding makes no difference to it.
        shell.SendMouse(Encoding.Latin1.GetBytes(report));
    }

    MouseModes Snapshot() =>
        new(
            surface.Parser.Modes.MouseTracking,
            surface.Parser.IsAlternateScreen,
            surface.Parser.Modes.AltScrollMode
        );

    // The wheel standing in for the cursor keys is still a pointer gesture, so it goes out on
    // the mouse path: it must not drag the view to the live edge the way typing does.
    void SendScrollKeys(bool up, int notches, DecModes modes)
    {
        var bytes = TerminalKeyEncoder.Encode(
            up ? Key.Up : Key.Down,
            KeyModifiers.None,
            modes.ApplicationCursorKeys
        );
        if (bytes == null)
        {
            return;
        }

        for (var i = 0; i < notches * linesPerNotch; i++)
        {
            shell.SendMouse(bytes);
        }
    }

    // X10 (mode 9) predates the modifier bits, so a report under it carries none. Shift never
    // reaches here - it routes local - but the mapping is written out in full anyway.
    static MouseModifiers Modifiers(KeyModifiers keys, MouseTrackingMode tracking)
    {
        if (tracking == MouseTrackingMode.X10)
        {
            return MouseModifiers.None;
        }

        var modifiers = MouseModifiers.None;
        if (keys.HasFlag(KeyModifiers.Shift))
        {
            modifiers |= MouseModifiers.Shift;
        }
        if (keys.HasFlag(KeyModifiers.Alt))
        {
            modifiers |= MouseModifiers.Alt;
        }
        if (keys.HasFlag(KeyModifiers.Control))
        {
            modifiers |= MouseModifiers.Ctrl;
        }
        return modifiers;
    }
}
