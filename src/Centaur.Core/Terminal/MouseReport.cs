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
/// Encodes mouse events into terminal reports. Currently the SGR (1006) form:
/// ESC [ &lt; {button} ; {col} ; {row} {M|m} with 1-based coordinates. The button
/// code carries modifier bits (+4 shift / +8 alt / +16 ctrl) and +32 for motion;
/// press/motion use 'M', release uses 'm'.
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
        var code = button switch
        {
            MouseButton.Left => 0,
            MouseButton.Middle => 1,
            MouseButton.Right => 2,
            MouseButton.ScrollUp => 64,
            MouseButton.ScrollDown => 65,
            _ => 3,
        };
        code += (int)modifiers;
        if (action == MouseAction.Motion)
        {
            code += 32;
        }
        var final = action == MouseAction.Release ? 'm' : 'M';
        return $"\x1b[<{code};{col + 1};{row + 1}{final}";
    }
}
