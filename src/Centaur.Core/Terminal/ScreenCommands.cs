namespace Centaur.Core.Terminal;

/// <summary>
/// Dispatch tables for the escape-sequence commands that only move the cursor or edit the
/// screen. Each entry maps onto <see cref="ScreenOps"/>; the commands that touch parser state
/// (SGR, device reports, the alternate screen) stay on the parser.
/// </summary>
static class ScreenCommands
{
    /// <summary>Executes a C0 control byte, returning false for one that is not a control the
    /// screen reacts to.</summary>
    public static bool TryExecuteControl(ScreenBuffer buffer, byte b)
    {
        switch (b)
        {
            case 0x07: // BEL - bell, ignore
                return true;
            case 0x08: // BS - backspace
                buffer.cursorX = Math.Max(0, buffer.cursorX - 1);
                return true;
            case 0x09: // TAB
                Tab(buffer);
                return true;
            case 0x0A: // LF - line feed
            case 0x0B: // VT - vertical tab
            case 0x0C: // FF - form feed
                ScreenOps.LineFeed(buffer);
                return true;
            case 0x0D: // CR - carriage return
                buffer.cursorX = 0;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Executes a CSI command that only moves the cursor or edits the screen,
    /// returning false for one the parser has to handle itself.</summary>
    public static bool TryExecuteCsi(ScreenBuffer buffer, char command, CsiArgs args, Cell blank) =>
        TryMoveCursorRelative(buffer, command, args)
        || TryMoveCursorAbsolute(buffer, command, args)
        || TryEdit(buffer, command, args, blank);

    static void Tab(ScreenBuffer buffer)
    {
        var next = ((buffer.cursorX / 8) + 1) * 8;
        buffer.cursorX = Math.Min(next, buffer.columns - 1);
    }

    static bool TryMoveCursorRelative(ScreenBuffer buffer, char command, CsiArgs args)
    {
        switch (command)
        {
            case 'A': // CUU - Cursor Up
                buffer.cursorY = Math.Max(0, buffer.cursorY - args.Get(0));
                return true;
            case 'B': // CUD - Cursor Down
                buffer.cursorY = Math.Min(buffer.rows - 1, buffer.cursorY + args.Get(0));
                return true;
            case 'C': // CUF - Cursor Forward
                buffer.cursorX = Math.Min(buffer.columns - 1, buffer.cursorX + args.Get(0));
                return true;
            case 'D': // CUB - Cursor Backward
                buffer.cursorX = Math.Max(0, buffer.cursorX - args.Get(0));
                return true;
            case 'E': // CNL - Cursor Next Line
                buffer.cursorX = 0;
                buffer.cursorY = Math.Min(buffer.rows - 1, buffer.cursorY + args.Get(0));
                return true;
            case 'F': // CPL - Cursor Previous Line
                buffer.cursorX = 0;
                buffer.cursorY = Math.Max(0, buffer.cursorY - args.Get(0));
                return true;
            default:
                return false;
        }
    }

    static bool TryMoveCursorAbsolute(ScreenBuffer buffer, char command, CsiArgs args)
    {
        switch (command)
        {
            case 'G': // CHA - Cursor Horizontal Absolute
                buffer.cursorX = Math.Clamp(args.Get(0) - 1, 0, buffer.columns - 1);
                return true;
            case 'd': // VPA - Vertical Position Absolute
                buffer.cursorY = Math.Clamp(args.Get(0) - 1, 0, buffer.rows - 1);
                return true;
            case 'H': // CUP - Cursor Position
            case 'f': // HVP - Horizontal Vertical Position
                buffer.cursorY = Math.Clamp(args.Get(0) - 1, 0, buffer.rows - 1);
                buffer.cursorX = Math.Clamp(args.Get(1, 1) - 1, 0, buffer.columns - 1);
                return true;
            default:
                return false;
        }
    }

    static bool TryEdit(ScreenBuffer buffer, char command, CsiArgs args, Cell blank)
    {
        switch (command)
        {
            case 'J': // ED - Erase in Display
                ScreenOps.EraseInDisplay(buffer, args.Get(0, 0), blank);
                return true;
            case 'K': // EL - Erase in Line
                ScreenOps.EraseInLine(buffer, args.Get(0, 0), blank);
                return true;
            case 'L': // IL - Insert Lines
                ScreenOps.InsertLines(buffer, args.Get(0), blank);
                return true;
            case 'M': // DL - Delete Lines
                ScreenOps.DeleteLines(buffer, args.Get(0), blank);
                return true;
            case 'P': // DCH - Delete Characters
                ScreenOps.DeleteCharacters(buffer, args.Get(0), blank);
                return true;
            case '@': // ICH - Insert Characters
                ScreenOps.InsertCharacters(buffer, args.Get(0), blank);
                return true;
            case 'X': // ECH - Erase Characters
                ScreenOps.EraseCharacters(buffer, args.Get(0), blank);
                return true;
            default:
                return false;
        }
    }
}
