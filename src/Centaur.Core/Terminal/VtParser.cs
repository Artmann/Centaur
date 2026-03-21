namespace Centaur.Core.Terminal;

public class VtParser
{
    readonly ScreenBuffer buffer;
    readonly TerminalTheme theme;
    uint currentFg;
    uint currentBg;

    enum State
    {
        Ground,
        Escape,
        Csi,
        CsiParam,
        Osc,
        OscEscape,
    }

    State state = State.Ground;
    readonly List<int> csiParams = new();
    int currentParam;

    // UTF-8 decoder state
    readonly byte[] utf8Buf = new byte[4];
    int utf8Remaining;
    int utf8Length;

    public VtParser(ScreenBuffer buffer)
        : this(buffer, CatppuccinThemes.Macchiato) { }

    public VtParser(ScreenBuffer buffer, TerminalTheme theme)
    {
        this.buffer = buffer;
        this.theme = theme;
        currentFg = theme.Foreground;
        currentBg = theme.Background;
    }

    public void Process(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            ProcessByte(b);
        }
    }

    void ProcessByte(byte b)
    {
        switch (state)
        {
            case State.Ground:
                ProcessGround(b);
                break;
            case State.Escape:
                ProcessEscape(b);
                break;
            case State.Csi:
            case State.CsiParam:
                ProcessCsi(b);
                break;
            case State.Osc:
                ProcessOsc(b);
                break;
            case State.OscEscape:
                // Any byte after ESC in OSC returns to ground (handles \x1b\\)
                state = State.Ground;
                break;
        }
    }

    void ProcessGround(byte b)
    {
        switch (b)
        {
            case 0x1B: // ESC
                state = State.Escape;
                break;
            case 0x07: // BEL - bell, ignore
                break;
            case 0x08: // BS - backspace
                if (buffer.cursorX > 0)
                {
                    buffer.cursorX--;
                }
                break;
            case 0x09: // TAB
                buffer.cursorX = ((buffer.cursorX / 8) + 1) * 8;
                if (buffer.cursorX >= buffer.columns)
                {
                    buffer.cursorX = buffer.columns - 1;
                }
                break;
            case 0x0A: // LF - line feed
            case 0x0B: // VT - vertical tab
            case 0x0C: // FF - form feed
                LineFeed();
                break;
            case 0x0D: // CR - carriage return
                buffer.cursorX = 0;
                break;
            default:
                if (utf8Remaining > 0 && (b & 0xC0) == 0x80)
                {
                    // UTF-8 continuation byte
                    utf8Buf[utf8Length++] = b;
                    utf8Remaining--;
                    if (utf8Remaining == 0)
                    {
                        FlushUtf8();
                    }
                }
                else if ((b & 0xE0) == 0xC0)
                {
                    // 2-byte UTF-8 start
                    utf8Buf[0] = b;
                    utf8Length = 1;
                    utf8Remaining = 1;
                }
                else if ((b & 0xF0) == 0xE0)
                {
                    // 3-byte UTF-8 start
                    utf8Buf[0] = b;
                    utf8Length = 1;
                    utf8Remaining = 2;
                }
                else if ((b & 0xF8) == 0xF0)
                {
                    // 4-byte UTF-8 start
                    utf8Buf[0] = b;
                    utf8Length = 1;
                    utf8Remaining = 3;
                }
                else if (b >= 0x20)
                {
                    // ASCII printable
                    WriteChar((char)b);
                }
                break;
        }
    }

    void ProcessEscape(byte b)
    {
        switch (b)
        {
            case (byte)'[': // CSI
                state = State.Csi;
                csiParams.Clear();
                currentParam = 0;
                break;
            case (byte)'D': // IND - Index (move down)
                LineFeed();
                state = State.Ground;
                break;
            case (byte)'E': // NEL - Next Line
                buffer.cursorX = 0;
                LineFeed();
                state = State.Ground;
                break;
            case (byte)'M': // RI - Reverse Index (move up)
                if (buffer.cursorY > 0)
                {
                    buffer.cursorY--;
                }
                else
                {
                    buffer.ScrollDown(1);
                }
                state = State.Ground;
                break;
            case (byte)']': // OSC - Operating System Command
                state = State.Osc;
                break;
            case (byte)'7': // DECSC - Save cursor
            case (byte)'8': // DECRC - Restore cursor
                // TODO: implement cursor save/restore
                state = State.Ground;
                break;
            default:
                // Unknown escape, return to ground
                state = State.Ground;
                break;
        }
    }

    void ProcessCsi(byte b)
    {
        if (b >= '0' && b <= '9')
        {
            currentParam = currentParam * 10 + (b - '0');
            state = State.CsiParam;
        }
        else if (b == ';')
        {
            csiParams.Add(currentParam);
            currentParam = 0;
            state = State.CsiParam;
        }
        else if (b >= 0x40 && b <= 0x7E)
        {
            // Final byte - execute command
            csiParams.Add(currentParam);
            ExecuteCsi((char)b);
            state = State.Ground;
        }
        else if (b == '?')
        {
            // Private mode indicator, ignore for now
            state = State.CsiParam;
        }
        else
        {
            // Unknown, return to ground
            state = State.Ground;
        }
    }

    void ExecuteCsi(char command)
    {
        int Param(int index, int defaultValue = 1) =>
            index < csiParams.Count && csiParams[index] > 0 ? csiParams[index] : defaultValue;

        switch (command)
        {
            case 'A': // CUU - Cursor Up
                buffer.cursorY = Math.Max(0, buffer.cursorY - Param(0));
                break;
            case 'B': // CUD - Cursor Down
                buffer.cursorY = Math.Min(buffer.rows - 1, buffer.cursorY + Param(0));
                break;
            case 'C': // CUF - Cursor Forward
                buffer.cursorX = Math.Min(buffer.columns - 1, buffer.cursorX + Param(0));
                break;
            case 'D': // CUB - Cursor Backward
                buffer.cursorX = Math.Max(0, buffer.cursorX - Param(0));
                break;
            case 'E': // CNL - Cursor Next Line
                buffer.cursorX = 0;
                buffer.cursorY = Math.Min(buffer.rows - 1, buffer.cursorY + Param(0));
                break;
            case 'F': // CPL - Cursor Previous Line
                buffer.cursorX = 0;
                buffer.cursorY = Math.Max(0, buffer.cursorY - Param(0));
                break;
            case 'G': // CHA - Cursor Horizontal Absolute
                buffer.cursorX = Math.Clamp(Param(0) - 1, 0, buffer.columns - 1);
                break;
            case 'H': // CUP - Cursor Position
            case 'f': // HVP - Horizontal Vertical Position
                buffer.cursorY = Math.Clamp(Param(0) - 1, 0, buffer.rows - 1);
                buffer.cursorX = Math.Clamp(Param(1, 1) - 1, 0, buffer.columns - 1);
                break;
            case 'J': // ED - Erase in Display
                EraseInDisplay(Param(0, 0));
                break;
            case 'K': // EL - Erase in Line
                EraseInLine(Param(0, 0));
                break;
            case 'L': // IL - Insert Lines
                InsertLines(Param(0));
                break;
            case 'M': // DL - Delete Lines
                DeleteLines(Param(0));
                break;
            case 'P': // DCH - Delete Characters
                DeleteCharacters(Param(0));
                break;
            case 'S': // SU - Scroll Up
                buffer.ScrollUp(Param(0));
                break;
            case 'T': // SD - Scroll Down
                buffer.ScrollDown(Param(0));
                break;
            case 'd': // VPA - Vertical Position Absolute
                buffer.cursorY = Math.Clamp(Param(0) - 1, 0, buffer.rows - 1);
                break;
            case 'm': // SGR - Select Graphic Rendition
                HandleSgr();
                break;
            case 'h': // SM - Set Mode
            case 'l': // RM - Reset Mode
                // TODO: handle modes (e.g., ?25h for cursor visibility)
                break;
            case 'r': // DECSTBM - Set Top and Bottom Margins
                // TODO: implement scroll region
                break;
        }
    }

    void FlushUtf8()
    {
        var span = utf8Buf.AsSpan(0, utf8Length);
        var chars = new char[2];
        var charCount = System.Text.Encoding.UTF8.GetChars(span, chars);
        for (var i = 0; i < charCount; i++)
        {
            WriteChar(chars[i]);
        }
    }

    void ProcessOsc(byte b)
    {
        if (b == 0x07)
        {
            // BEL terminates OSC
            state = State.Ground;
        }
        else if (b == 0x1B)
        {
            // Could be start of ST (\x1b\\)
            state = State.OscEscape;
        }
        // Otherwise consume and discard
    }

    Cell DefaultCell() => new(' ', theme.Foreground, theme.Background);

    void WriteChar(char c)
    {
        if (buffer.cursorX >= buffer.columns)
        {
            buffer.cursorX = 0;
            LineFeed();
        }
        buffer[buffer.cursorX, buffer.cursorY] = new Cell(c, currentFg, currentBg);
        buffer.cursorX++;
    }

    void LineFeed()
    {
        if (buffer.cursorY < buffer.rows - 1)
        {
            buffer.cursorY++;
        }
        else
        {
            buffer.ScrollUp(1);
        }
    }

    void EraseInDisplay(int mode)
    {
        switch (mode)
        {
            case 0: // Erase from cursor to end of screen
                EraseInLine(0);
                for (int y = buffer.cursorY + 1; y < buffer.rows; y++)
                {
                    for (int x = 0; x < buffer.columns; x++)
                    {
                        buffer[x, y] = DefaultCell();
                    }
                }
                break;
            case 1: // Erase from start of screen to cursor
                for (int y = 0; y < buffer.cursorY; y++)
                {
                    for (int x = 0; x < buffer.columns; x++)
                    {
                        buffer[x, y] = DefaultCell();
                    }
                }
                EraseInLine(1);
                break;
            case 2: // Erase entire screen
            case 3: // Erase entire screen and scrollback
                buffer.Clear();
                break;
        }
    }

    void EraseInLine(int mode)
    {
        switch (mode)
        {
            case 0: // Erase from cursor to end of line
                for (int x = buffer.cursorX; x < buffer.columns; x++)
                {
                    buffer[x, buffer.cursorY] = DefaultCell();
                }
                break;
            case 1: // Erase from start of line to cursor
                for (int x = 0; x <= buffer.cursorX; x++)
                {
                    buffer[x, buffer.cursorY] = DefaultCell();
                }
                break;
            case 2: // Erase entire line
                for (int x = 0; x < buffer.columns; x++)
                {
                    buffer[x, buffer.cursorY] = DefaultCell();
                }
                break;
        }
    }

    void InsertLines(int count)
    {
        // Scroll lines down from cursor position
        for (int i = 0; i < count && buffer.cursorY + i < buffer.rows; i++)
        {
            for (int y = buffer.rows - 1; y > buffer.cursorY; y--)
            {
                for (int x = 0; x < buffer.columns; x++)
                {
                    buffer[x, y] = buffer[x, y - 1];
                }
            }
            for (int x = 0; x < buffer.columns; x++)
            {
                buffer[x, buffer.cursorY] = DefaultCell();
            }
        }
    }

    void DeleteLines(int count)
    {
        // Scroll lines up from cursor position
        for (int i = 0; i < count && buffer.cursorY + i < buffer.rows; i++)
        {
            for (int y = buffer.cursorY; y < buffer.rows - 1; y++)
            {
                for (int x = 0; x < buffer.columns; x++)
                {
                    buffer[x, y] = buffer[x, y + 1];
                }
            }
            for (int x = 0; x < buffer.columns; x++)
            {
                buffer[x, buffer.rows - 1] = DefaultCell();
            }
        }
    }

    void DeleteCharacters(int count)
    {
        for (int i = 0; i < count; i++)
        {
            for (int x = buffer.cursorX; x < buffer.columns - 1; x++)
            {
                buffer[x, buffer.cursorY] = buffer[x + 1, buffer.cursorY];
            }
            buffer[buffer.columns - 1, buffer.cursorY] = DefaultCell();
        }
    }

    void HandleSgr()
    {
        for (int i = 0; i < csiParams.Count; i++)
        {
            var p = csiParams[i];
            switch (p)
            {
                case 0:
                    currentFg = theme.Foreground;
                    currentBg = theme.Background;
                    break;
                case >= 30 and <= 37:
                    currentFg = theme.GetColor(p - 30);
                    break;
                case 39:
                    currentFg = theme.Foreground;
                    break;
                case >= 40 and <= 47:
                    currentBg = theme.GetColor(p - 40);
                    break;
                case 49:
                    currentBg = theme.Background;
                    break;
                case >= 90 and <= 97:
                    currentFg = theme.GetColor(p - 90 + 8);
                    break;
                case >= 100 and <= 107:
                    currentBg = theme.GetColor(p - 100 + 8);
                    break;
                case 38:
                    i = ParseExtendedColor(i, isForeground: true);
                    break;
                case 48:
                    i = ParseExtendedColor(i, isForeground: false);
                    break;
            }
        }
    }

    int ParseExtendedColor(int i, bool isForeground)
    {
        if (i + 1 >= csiParams.Count)
        {
            return i;
        }
        var mode = csiParams[i + 1];

        if (mode == 5 && i + 2 < csiParams.Count)
        {
            var colorIndex = csiParams[i + 2];
            var color = theme.GetColor(colorIndex);
            if (isForeground)
            {
                currentFg = color;
            }
            else
            {
                currentBg = color;
            }
            return i + 2;
        }
        else if (mode == 2 && i + 4 < csiParams.Count)
        {
            var r = (byte)csiParams[i + 2];
            var g = (byte)csiParams[i + 3];
            var b = (byte)csiParams[i + 4];
            var color = 0xFF000000u | ((uint)r << 16) | ((uint)g << 8) | b;
            if (isForeground)
            {
                currentFg = color;
            }
            else
            {
                currentBg = color;
            }
            return i + 4;
        }
        return i + 1;
    }
}
