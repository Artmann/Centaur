namespace Centaur.Core.Terminal;

public enum MouseButton
{
    Left,
    Middle,
    Right,
    ScrollUp,
    ScrollDown,
    None,
}

public enum MouseAction
{
    Press,
    Release,
    Motion,
}

[Flags]
public enum MouseModifiers
{
    None = 0,
    Shift = 4,
    Alt = 8,
    Ctrl = 16,
}

public enum MouseTrackingMode
{
    Off,
    X10,
    Normal,
    ButtonEvent,
    AnyEvent,
}

/// <summary>
/// Encodes mouse events into terminal reports, in the two forms a program can ask for.
///
/// SGR (1006) is ESC [ &lt; {button} ; {col} ; {row} {M|m} with 1-based coordinates, and is what
/// anything modern enables. The legacy X10 form, ESC [ M Cb Cx Cy with every field offset by
/// 32, is the fallback for a program that turns tracking on without asking for SGR.
///
/// Both share the button code: left 0, middle 1, right 2, wheel up 64, wheel down 65, plus the
/// modifier bits (+4 shift / +8 alt / +16 ctrl) and +32 for motion.
/// </summary>
public static class MouseReport
{
    public static string EncodeSgr(
        MouseButton button,
        int col,
        int row,
        MouseAction action,
        MouseModifiers modifiers
    )
    {
        var code = ButtonCode(button) + (int)modifiers;
        if (action == MouseAction.Motion)
        {
            code += 32;
        }
        var final = action == MouseAction.Release ? 'm' : 'M';
        return $"\x1b[<{code};{col + 1};{row + 1}{final}";
    }

    /// <summary>
    /// The legacy form, used when the program enabled tracking but not SGR. Every field is its
    /// value plus 32 so the report stays printable, which is also the format's ceiling: a
    /// coordinate past 223 would need a byte outside Latin-1, so a wide window reports the last
    /// encodable cell rather than wrapping onto a different one. Callers must put the result on
    /// the wire as Latin-1, not UTF-8, or those high bytes become two bytes each.
    /// </summary>
    public static string EncodeX10(
        MouseButton button,
        int col,
        int row,
        MouseAction action,
        MouseModifiers modifiers
    )
    {
        // X10 has no per-button release - every release is the "no button" code.
        var code = action == MouseAction.Release ? noButton : ButtonCode(button);
        code += (int)modifiers;
        if (action == MouseAction.Motion)
        {
            code += 32;
        }
        return $"\x1b[M{(char)(code + offset)}{Coordinate(col)}{Coordinate(row)}";
    }

    const int offset = 32;
    const int noButton = 3;
    const int lastEncodableCoordinate = 223;

    static char Coordinate(int value) =>
        (char)(Math.Clamp(value + 1, 1, lastEncodableCoordinate) + offset);

    static int ButtonCode(MouseButton button) =>
        button switch
        {
            MouseButton.Left => 0,
            MouseButton.Middle => 1,
            MouseButton.Right => 2,
            MouseButton.ScrollUp => 64,
            MouseButton.ScrollDown => 65,
            _ => noButton,
        };
}
