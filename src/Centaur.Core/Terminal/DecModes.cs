namespace Centaur.Core.Terminal;

/// <summary>
/// The DEC private modes set and reset by CSI ? Ps h/l, and reported back by DECRQM. Pure
/// state: the one mode with a side effect, 1049 (alternate screen), stays on the parser
/// because it swaps the screen buffer.
/// </summary>
public sealed class DecModes
{
    public bool ApplicationCursorKeys { get; private set; } // 1
    public bool CursorVisible { get; private set; } = true; // 25
    public MouseTrackingMode MouseTracking { get; private set; } // 9/1000/1002/1003
    public bool FocusEventMode { get; private set; } // 1004
    public bool MouseSgrMode { get; private set; } // 1006
    public bool BracketedPasteMode { get; private set; } // 2004

    // 1007, on unless a program turns it off - the xterm default. This is what lets the wheel
    // reach a full-screen program that never asked for mouse tracking, so defaulting it off
    // would leave the common case (a pager on the alternate screen) unscrollable.
    public bool AltScrollMode { get; private set; } = true;

    /// <summary>Applies one mode, returning false for the ones held elsewhere.</summary>
    internal bool TrySet(int mode, bool enabled)
    {
        if (TrySetMouse(mode, enabled))
        {
            return true;
        }

        switch (mode)
        {
            case 1: // DECCKM - Application Cursor Keys
                ApplicationCursorKeys = enabled;
                return true;
            case 25: // DECTCEM - Cursor Visibility
                CursorVisible = enabled;
                return true;
            case 2004: // Bracketed Paste Mode
                BracketedPasteMode = enabled;
                return true;
            default:
                return false;
        }
    }

    bool TrySetMouse(int mode, bool enabled)
    {
        switch (mode)
        {
            case 9: // X10 compatibility tracking - presses only, no modifiers
            case 1000: // Normal mouse tracking (X11)
            case 1002: // Button-event tracking
            case 1003: // Any-event tracking
                MouseTracking = enabled ? TrackingFor(mode) : MouseTrackingMode.Off;
                return true;
            case 1004: // Focus event reporting
                FocusEventMode = enabled;
                return true;
            case 1006: // SGR extended mouse mode
                MouseSgrMode = enabled;
                return true;
            case 1007: // Alternate scroll mode
                AltScrollMode = enabled;
                return true;
            default:
                return false;
        }
    }

    static MouseTrackingMode TrackingFor(int mode) =>
        mode switch
        {
            9 => MouseTrackingMode.X10,
            1002 => MouseTrackingMode.ButtonEvent,
            1003 => MouseTrackingMode.AnyEvent,
            _ => MouseTrackingMode.Normal,
        };

    /// <summary>DECRQM reply state: 0 = not recognized, 1 = set, 2 = reset. The alternate
    /// screen is reported too, from the flag the parser keeps.
    ///
    /// The mouse modes have to answer too, in <see cref="ReportMouse"/>: a program told "not
    /// recognized" concludes the terminal has no mouse support and turns the feature off. 1005
    /// and 1015 stay absent on purpose - those encodings are not implemented, and saying so is
    /// what keeps a program from picking a form we cannot speak.</summary>
    internal int Report(int mode, bool alternateScreen) =>
        mode switch
        {
            1 => ApplicationCursorKeys ? 1 : 2,
            25 => CursorVisible ? 1 : 2,
            1049 => alternateScreen ? 1 : 2,
            2004 => BracketedPasteMode ? 1 : 2,
            _ => ReportMouse(mode),
        };

    int ReportMouse(int mode) =>
        mode switch
        {
            // The tracking levels share one field, so only the level currently selected
            // reports as set - asking about 1000 while 1003 is on answers "reset".
            9 or 1000 or 1002 or 1003 => MouseTracking == TrackingFor(mode) ? 1 : 2,
            1004 => FocusEventMode ? 1 : 2,
            1006 => MouseSgrMode ? 1 : 2,
            1007 => AltScrollMode ? 1 : 2,
            _ => 0,
        };
}
