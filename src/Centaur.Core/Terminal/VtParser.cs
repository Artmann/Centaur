using System.Reflection;

namespace Centaur.Core.Terminal;

public class VtParser
{
    readonly ScreenBuffer mainBuffer;
    readonly ScreenBuffer alternateBuffer;
    ScreenBuffer buffer;
    readonly TerminalTheme theme;
    uint currentFg;
    uint currentBg;

    // Current SGR text-style "pen" applied to each printed cell.
    bool currentBold;
    bool currentFaint;
    bool currentItalic;
    UnderlineStyle currentUnderline;
    uint currentUnderlineColor;
    bool currentBlink;
    bool currentInverse;
    bool currentInvisible;
    bool currentStrikethrough;
    bool currentOverline;

    // DEC Private Mode state
    public bool CursorVisible { get; private set; } = true;
    public bool ApplicationCursorKeys { get; private set; }
    public bool BracketedPasteMode { get; private set; }
    public bool IsAlternateScreen { get; private set; }

    // Mouse reporting modes.
    public MouseTrackingMode MouseTracking { get; private set; } // 1000/1002/1003
    public bool MouseSgrMode { get; private set; } // 1006
    public bool FocusEventMode { get; private set; } // 1004
    public bool AltScrollMode { get; private set; } // 1007
    public ScreenBuffer ActiveBuffer => buffer;

    // Response channel back to the PTY for queries (DA, DECRQM, OSC color/clipboard
    // reads). Subscribers receive the raw bytes to write to the pty's input.
    public event Action<byte[]>? Respond;

    void Reply(string s) => Respond?.Invoke(System.Text.Encoding.Latin1.GetBytes(s));

    // Version reported by XTVERSION. Resolved once from the assembly's build version
    // (set in Directory.Build.props) so it tracks releases instead of a hardcoded literal.
    static readonly string terminalVersion = ResolveVersion();

    static string ResolveVersion()
    {
        var info = typeof(VtParser)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // Strip any "+<gitsha>" build metadata SourceLink may have appended.
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
        var version = typeof(VtParser).Assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    // OSC-driven state.
    public string? WindowTitle { get; private set; } // OSC 0/2
    public string? IconName { get; private set; } // OSC 0/1
    public string? WorkingDirectory { get; private set; } // OSC 7
    public uint[] Palette { get; } = new uint[256]; // OSC 4/104
    public uint DefaultForeground { get; private set; } // OSC 10
    public uint DefaultBackground { get; private set; } // OSC 11
    public int? LastExitCode { get; private set; } // OSC 133;D;<code>

    // Fired for OSC 52 clipboard writes/clears (read requests use Respond instead).
    public event Action<ClipboardRequest>? ClipboardChanged;

    // Active OSC 8 hyperlink target applied to printed cells (null when none).
    string? currentHyperlink;

    // Saved cursor state (DECSC/DECRC). Per-screen: the main and alternate buffers
    // each have their own register, matching xterm. A full-screen app's save/restore
    // on the alternate screen must not corrupt the main screen's cursor, which is
    // saved on 1049h and restored on 1049l.
    struct SavedCursor
    {
        public int x;
        public int y;
        public uint fg;
        public uint bg;
    }

    SavedCursor mainSaved;
    SavedCursor altSaved;

    ref SavedCursor CurrentSaved()
    {
        if (buffer == alternateBuffer)
        {
            return ref altSaved;
        }
        return ref mainSaved;
    }

    void SaveCursor()
    {
        ref var slot = ref CurrentSaved();
        slot.x = buffer.cursorX;
        slot.y = buffer.cursorY;
        slot.fg = currentFg;
        slot.bg = currentBg;
    }

    void RestoreCursor()
    {
        ref var slot = ref CurrentSaved();
        buffer.cursorX = slot.x;
        buffer.cursorY = slot.y;
        currentFg = slot.fg;
        currentBg = slot.bg;
    }

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

    // Parallel to csiParams: true when the separator before that param was a
    // colon (':'), marking it as a sub-parameter of the preceding param. Used
    // by SGR to distinguish ESC[4:3m (curly underline) from ESC[4;3m
    // (underline + italic).
    readonly List<bool> csiParamIsColon = new();
    bool pendingColon;
    int currentParam;

    // CSI private prefix ('?', '>', '=', '<') and intermediate byte (e.g. '$'
    // for DECRQM). 0 when absent. Reset at the start of each CSI sequence.
    char csiPrefix;
    char csiIntermediate;

    // OSC payload accumulator (bytes between ESC] and the terminator).
    readonly List<byte> oscBuffer = new();

    // UTF-8 decoder state
    readonly byte[] utf8Buf = new byte[4];
    int utf8Remaining;
    int utf8Length;

    public VtParser(ScreenBuffer buffer)
        : this(buffer, CatppuccinThemes.Macchiato) { }

    public VtParser(ScreenBuffer buffer, TerminalTheme theme)
    {
        this.mainBuffer = buffer;
        this.alternateBuffer = new ScreenBuffer(
            buffer.columns,
            buffer.rows,
            theme,
            enableScrollback: false
        );
        this.buffer = buffer;
        this.theme = theme;
        currentFg = theme.Foreground;
        currentBg = theme.Background;
        DefaultForeground = theme.Foreground;
        DefaultBackground = theme.Background;
        for (int i = 0; i < Palette.Length; i++)
        {
            Palette[i] = theme.GetColor(i);
        }
    }

    public void Resize(int columns, int rows)
    {
        mainBuffer.Resize(columns, rows);
        alternateBuffer.Resize(columns, rows);
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
                // ESC \ (ST) terminates the OSC; any other byte just ends it.
                if (b == (byte)'\\')
                {
                    DispatchOsc();
                }
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
                csiParamIsColon.Clear();
                pendingColon = false;
                currentParam = 0;
                csiPrefix = '\0';
                csiIntermediate = '\0';
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
                if (buffer.cursorY == buffer.scrollTop)
                {
                    buffer.ScrollDownInRegion(1, buffer.scrollTop, buffer.scrollBottom);
                }
                else if (buffer.cursorY > 0)
                {
                    buffer.cursorY--;
                }
                state = State.Ground;
                break;
            case (byte)']': // OSC - Operating System Command
                state = State.Osc;
                oscBuffer.Clear();
                break;
            case (byte)'7': // DECSC - Save cursor
                SaveCursor();
                state = State.Ground;
                break;
            case (byte)'8': // DECRC - Restore cursor
                RestoreCursor();
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
            PushParam();
            pendingColon = false;
            state = State.CsiParam;
        }
        else if (b == ':')
        {
            // Colon sub-parameter: the next param belongs to this param's group.
            PushParam();
            pendingColon = true;
            state = State.CsiParam;
        }
        else if (b >= 0x40 && b <= 0x7E)
        {
            // Final byte - execute command
            PushParam();
            ExecuteCsi((char)b);
            state = State.Ground;
        }
        else if (b >= 0x3C && b <= 0x3F)
        {
            // Private parameter prefix: '<' '=' '>' '?'
            csiPrefix = (char)b;
            state = State.CsiParam;
        }
        else if (b >= 0x20 && b <= 0x2F)
        {
            // Intermediate byte (e.g. '$' in DECRQM's CSI ? Ps $ p).
            csiIntermediate = (char)b;
            state = State.CsiParam;
        }
        else
        {
            // Unknown, return to ground
            state = State.Ground;
        }
    }

    void PushParam()
    {
        csiParams.Add(currentParam);
        csiParamIsColon.Add(pendingColon);
        currentParam = 0;
    }

    void ExecuteCsi(char command)
    {
        // Private/prefixed CSI ( '<' '=' '>' '?' ) must not fall through to the ANSI
        // cursor/SGR handlers. Kitty-keyboard 'CSI > u' / 'CSI < u' / 'CSI = u' and
        // XTMODKEYS 'CSI > m' would otherwise hijack RCP/SGR and move the cursor.
        if (csiPrefix != '\0')
        {
            ExecutePrivateCsi(command);
            return;
        }

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
            case '@': // ICH - Insert Characters
                InsertCharacters(Param(0));
                break;
            case 'X': // ECH - Erase Characters
                EraseCharacters(Param(0));
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
            case 'c': // DA1 - primary Device Attributes (unprefixed)
                HandleDeviceAttributes();
                break;
            case 'n': // DSR - Device Status Report
                HandleDeviceStatus();
                break;
            case 's': // SCP - Save Cursor Position (ANSI)
                SaveCursor();
                break;
            case 'u': // RCP - Restore Cursor Position (ANSI)
                RestoreCursor();
                break;
            case 'r': // DECSTBM - Set Top and Bottom Margins
            {
                var top = Param(0) - 1; // Convert 1-based to 0-based
                var bottom = Param(1, buffer.rows) - 1;
                buffer.SetScrollRegion(top, bottom);
                buffer.cursorX = 0;
                buffer.cursorY = 0;
                break;
            }
        }
    }

    // Dispatch a CSI sequence that carried a private prefix ('<' '=' '>' '?').
    // Only the prefix-aware commands act; everything else (notably Kitty-keyboard
    // 'u', XTMODKEYS 'm', DSR 'n', prefixed 's') is ignored so it cannot reach the
    // ANSI cursor/SGR handlers.
    void ExecutePrivateCsi(char command)
    {
        switch (command)
        {
            case 'c': // DA2 ('>') / DA3 ('=')
                HandleDeviceAttributes();
                break;
            case 'h': // SM - Set Mode (DEC private)
            case 'l': // RM - Reset Mode (DEC private)
                if (csiPrefix == '?')
                {
                    ExecuteDecMode(command);
                }
                break;
            case 'p': // DECRQM - Request Mode (CSI ? Ps $ p)
                if (csiPrefix == '?' && csiIntermediate == '$')
                {
                    HandleDecrqm();
                }
                break;
            case 'q': // XTVERSION - report terminal name/version (CSI > q)
                if (csiPrefix == '>' && csiIntermediate == '\0')
                {
                    Reply($"\x1bP>|Centaur({terminalVersion})\x1b\\");
                }
                break;
        }
    }

    void ExecuteDecMode(char command)
    {
        var enabled = command == 'h';
        for (int i = 0; i < csiParams.Count; i++)
        {
            switch (csiParams[i])
            {
                case 1: // DECCKM - Application Cursor Keys
                    ApplicationCursorKeys = enabled;
                    break;
                case 25: // DECTCEM - Cursor Visibility
                    CursorVisible = enabled;
                    break;
                case 1000: // Normal mouse tracking (X11)
                    MouseTracking = enabled ? MouseTrackingMode.Normal : MouseTrackingMode.Off;
                    break;
                case 1002: // Button-event tracking
                    MouseTracking = enabled ? MouseTrackingMode.ButtonEvent : MouseTrackingMode.Off;
                    break;
                case 1003: // Any-event tracking
                    MouseTracking = enabled ? MouseTrackingMode.AnyEvent : MouseTrackingMode.Off;
                    break;
                case 1004: // Focus event reporting
                    FocusEventMode = enabled;
                    break;
                case 1006: // SGR extended mouse mode
                    MouseSgrMode = enabled;
                    break;
                case 1007: // Alternate scroll mode
                    AltScrollMode = enabled;
                    break;
                case 2004: // Bracketed Paste Mode
                    BracketedPasteMode = enabled;
                    break;
                case 1049: // Alternate Screen Buffer
                    if (enabled)
                    {
                        // Save the main-screen cursor (into the main register, since the
                        // main buffer is still active), switch to alternate, clear.
                        SaveCursor();
                        buffer = alternateBuffer;
                        buffer.Clear();
                        buffer.SetScrollRegion(0, buffer.rows - 1);
                        IsAlternateScreen = true;
                    }
                    else
                    {
                        // Switch back to main, then restore from the main register. The
                        // app's save/restore on the alternate screen used altSaved, so the
                        // main-screen cursor saved on 1049h is intact.
                        buffer = mainBuffer;
                        RestoreCursor();
                        IsAlternateScreen = false;
                    }
                    break;
            }
        }
    }

    void HandleDeviceAttributes()
    {
        switch (csiPrefix)
        {
            case '>': // DA2 - secondary: device type 1, firmware 0, rom 0
                Reply("\x1b[>1;0;0c");
                break;
            case '=': // DA3 - tertiary: unit id, as DCS ! | <hex> ST
                Reply("\x1bP!|00000000\x1b\\");
                break;
            default: // DA1 - primary: VT220 (62) + ansi color (22)
                Reply("\x1b[?62;22c");
                break;
        }
    }

    void HandleDeviceStatus()
    {
        var request = csiParams.Count > 0 ? csiParams[0] : 0;
        switch (request)
        {
            case 5: // Report device status: terminal is functioning correctly.
                Reply("\x1b[0n");
                break;
            case 6: // CPR - report cursor position as 1-based row;col.
                Reply($"\x1b[{buffer.cursorY + 1};{buffer.cursorX + 1}R");
                break;
        }
    }

    void HandleDecrqm()
    {
        var mode = csiParams.Count > 0 ? csiParams[0] : 0;
        // Reply state: 0 = not recognized, 1 = set, 2 = reset.
        var modeState = mode switch
        {
            1 => ApplicationCursorKeys ? 1 : 2,
            25 => CursorVisible ? 1 : 2,
            2004 => BracketedPasteMode ? 1 : 2,
            1049 => IsAlternateScreen ? 1 : 2,
            _ => 0,
        };
        Reply($"\x1b[?{mode};{modeState}$y");
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
            DispatchOsc();
            state = State.Ground;
        }
        else if (b == 0x1B)
        {
            // Could be start of ST (\x1b\\)
            state = State.OscEscape;
        }
        else
        {
            oscBuffer.Add(b);
        }
    }

    void DispatchOsc()
    {
        if (oscBuffer.Count == 0)
        {
            return;
        }
        var text = System.Text.Encoding.UTF8.GetString(oscBuffer.ToArray());
        var semi = text.IndexOf(';');
        var codeStr = semi >= 0 ? text[..semi] : text;
        var rest = semi >= 0 ? text[(semi + 1)..] : "";
        if (!int.TryParse(codeStr, out var code))
        {
            return;
        }

        switch (code)
        {
            case 0: // set both window title and icon name
                WindowTitle = rest;
                IconName = rest;
                break;
            case 1: // set icon name
                IconName = rest;
                break;
            case 2: // set window title
                WindowTitle = rest;
                break;
            case 4: // set/query a palette color
                HandleOscPaletteColor(rest);
                break;
            case 7: // report working directory
                WorkingDirectory = rest;
                break;
            case 8: // hyperlink
                HandleOscHyperlink(rest);
                break;
            case 10: // set/query default foreground
                HandleOscDynamicColor(rest, ColorTarget.Foreground);
                break;
            case 11: // set/query default background
                HandleOscDynamicColor(rest, ColorTarget.Background);
                break;
            case 52: // clipboard
                HandleOscClipboard(rest);
                break;
            case 104: // reset palette colors
                HandleOscResetPalette(rest);
                break;
            case 133: // semantic prompt mark
                HandleSemanticPrompt(rest);
                break;
        }
    }

    void HandleOscPaletteColor(string rest)
    {
        // "{index};{spec-or-?}"
        var semi = rest.IndexOf(';');
        if (semi < 0)
        {
            return;
        }
        if (!int.TryParse(rest[..semi], out var index) || index < 0 || index >= Palette.Length)
        {
            return;
        }
        var spec = rest[(semi + 1)..];
        if (spec == "?")
        {
            Reply($"\x1b]4;{index};{FormatColor(Palette[index])}\x07");
            return;
        }
        if (TryParseXColor(spec, out var color))
        {
            Palette[index] = color;
        }
    }

    void HandleOscResetPalette(string rest)
    {
        // "104" alone resets all; "104;n" resets index n to the theme default.
        if (rest.Length == 0)
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                Palette[i] = theme.GetColor(i);
            }
            return;
        }
        if (int.TryParse(rest, out var index) && index >= 0 && index < Palette.Length)
        {
            Palette[index] = theme.GetColor(index);
        }
    }

    void HandleOscDynamicColor(string spec, ColorTarget target)
    {
        if (spec == "?")
        {
            var current = target == ColorTarget.Foreground ? DefaultForeground : DefaultBackground;
            var code = target == ColorTarget.Foreground ? 10 : 11;
            Reply($"\x1b]{code};{FormatColor(current)}\x07");
            return;
        }
        if (TryParseXColor(spec, out var color))
        {
            if (target == ColorTarget.Foreground)
            {
                DefaultForeground = color;
            }
            else
            {
                DefaultBackground = color;
            }
        }
    }

    void HandleOscHyperlink(string rest)
    {
        // "8;{params};{uri}" — empty uri ends the current hyperlink.
        var semi = rest.IndexOf(';');
        var uri = semi >= 0 ? rest[(semi + 1)..] : "";
        currentHyperlink = uri.Length > 0 ? uri : null;
    }

    void HandleOscClipboard(string rest)
    {
        // "{selection};{base64-or-?}" — selection defaults to 'c'.
        var semi = rest.IndexOf(';');
        var selectionField = semi >= 0 ? rest[..semi] : "";
        var data = semi >= 0 ? rest[(semi + 1)..] : "";
        var selection = selectionField.Length > 0 ? selectionField[0] : 'c';
        if (data == "?")
        {
            // Read request: reply with empty contents (no clipboard wired yet).
            Reply($"\x1b]52;{selection};\x07");
            return;
        }
        ClipboardChanged?.Invoke(new ClipboardRequest(selection, data));
    }

    void HandleSemanticPrompt(string rest)
    {
        // rest is "A", "B", "C", or "D[;exitcode]".
        var kind = rest.Length > 0 ? rest[0] : '\0';
        switch (kind)
        {
            case 'A':
                buffer.SetMark(buffer.cursorY, PromptMark.Prompt);
                break;
            case 'B':
                buffer.SetMark(buffer.cursorY, PromptMark.Command);
                break;
            case 'C':
                buffer.SetMark(buffer.cursorY, PromptMark.Output);
                break;
            case 'D':
                var semi = rest.IndexOf(';');
                if (semi >= 0 && int.TryParse(rest[(semi + 1)..], out var exit))
                {
                    LastExitCode = exit;
                }
                break;
        }
    }

    // Parses an X11 "rgb:rr/gg/bb" (or "rrrr/gggg/bbbb") color spec into ARGB.
    static bool TryParseXColor(string spec, out uint color)
    {
        color = 0;
        if (!spec.StartsWith("rgb:", StringComparison.Ordinal))
        {
            return false;
        }
        var parts = spec[4..].Split('/');
        if (parts.Length != 3)
        {
            return false;
        }
        Span<byte> rgb = stackalloc byte[3];
        for (int i = 0; i < 3; i++)
        {
            var p = parts[i];
            if (
                p.Length is < 1 or > 4
                || !uint.TryParse(p, System.Globalization.NumberStyles.HexNumber, null, out var v)
            )
            {
                return false;
            }
            // X11 scales each channel so its width's max maps to 0xff: 1-digit 'f'
            // -> 0xff (not 0x0f), 4-digit 0xffff -> 0xff, etc. Scale proportionally
            // with rounding rather than a bare right-shift.
            var max = (1u << (p.Length * 4)) - 1;
            rgb[i] = (byte)(((v * 255) + (max / 2)) / max);
        }
        color = 0xFF000000u | ((uint)rgb[0] << 16) | ((uint)rgb[1] << 8) | rgb[2];
        return true;
    }

    // Formats ARGB as the X11 "rgb:rrrr/gggg/bbbb" reply form (16-bit channels).
    static string FormatColor(uint argb)
    {
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        return $"rgb:{r:x2}{r:x2}/{g:x2}{g:x2}/{b:x2}{b:x2}";
    }

    Cell DefaultCell() => new(' ', theme.Foreground, theme.Background);

    void WriteChar(char c)
    {
        if (buffer.cursorX >= buffer.columns)
        {
            buffer.cursorX = 0;
            LineFeed();
        }
        buffer[buffer.cursorX, buffer.cursorY] = new Cell(c, currentFg, currentBg)
        {
            bold = currentBold,
            faint = currentFaint,
            italic = currentItalic,
            underline = currentUnderline,
            underlineColor = currentUnderlineColor,
            blink = currentBlink,
            inverse = currentInverse,
            invisible = currentInvisible,
            strikethrough = currentStrikethrough,
            overline = currentOverline,
            hyperlink = currentHyperlink,
        };
        buffer.cursorX++;
    }

    void LineFeed()
    {
        if (buffer.ScrollOffset > 0)
        {
            buffer.ScrollToBottom();
        }

        if (buffer.cursorY == buffer.scrollBottom)
        {
            buffer.ScrollUpInRegion(1, buffer.scrollTop, buffer.scrollBottom);
        }
        else if (buffer.cursorY < buffer.rows - 1)
        {
            buffer.cursorY++;
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
            case 2: // Erase entire screen (preserve cursor)
                buffer.ClearCells();
                break;
            case 3: // Erase entire screen and scrollback
                buffer.ClearCells();
                buffer.ClearScrollback();
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

    void InsertCharacters(int count)
    {
        // Shift characters right from cursor position
        for (int x = buffer.columns - 1; x >= buffer.cursorX + count; x--)
        {
            buffer[x, buffer.cursorY] = buffer[x - count, buffer.cursorY];
        }
        // Clear inserted positions
        for (int x = buffer.cursorX; x < Math.Min(buffer.cursorX + count, buffer.columns); x++)
        {
            buffer[x, buffer.cursorY] = DefaultCell();
        }
    }

    void EraseCharacters(int count)
    {
        // Erase characters at cursor position (doesn't shift)
        for (int x = buffer.cursorX; x < Math.Min(buffer.cursorX + count, buffer.columns); x++)
        {
            buffer[x, buffer.cursorY] = DefaultCell();
        }
    }

    // SGR target for an extended-color attribute (38/48/58).
    enum ColorTarget
    {
        Foreground,
        Background,
        Underline,
    }

    void HandleSgr()
    {
        // Group params so colon sub-parameters attach to their primary param:
        // ESC[4:3m -> one group [4,3]; ESC[38;2;1;2;3m -> groups [38],[2],[1],[2],[3].
        var groups = new List<List<int>>();
        for (int k = 0; k < csiParams.Count; k++)
        {
            if (k == 0 || !csiParamIsColon[k])
            {
                groups.Add(new List<int> { csiParams[k] });
            }
            else
            {
                groups[^1].Add(csiParams[k]);
            }
        }

        for (int g = 0; g < groups.Count; g++)
        {
            var group = groups[g];
            var p = group[0];
            switch (p)
            {
                case 0:
                    ResetStyles();
                    break;
                case 1:
                    currentBold = true;
                    break;
                case 2:
                    currentFaint = true;
                    break;
                case 3:
                    currentItalic = true;
                    break;
                case 4:
                    // ESC[4m is single; ESC[4:Nm selects the style by sub-param.
                    currentUnderline =
                        group.Count > 1 ? MapUnderline(group[1]) : UnderlineStyle.Single;
                    break;
                case 5:
                case 6: // Ghostty treats rapid blink (6) the same as blink (5).
                    currentBlink = true;
                    break;
                case 7:
                    currentInverse = true;
                    break;
                case 8:
                    currentInvisible = true;
                    break;
                case 9:
                    currentStrikethrough = true;
                    break;
                case 21:
                    currentUnderline = UnderlineStyle.Double;
                    break;
                case 22: // resets both bold and faint
                    currentBold = false;
                    currentFaint = false;
                    break;
                case 23:
                    currentItalic = false;
                    break;
                case 24:
                    currentUnderline = UnderlineStyle.None;
                    break;
                case 25:
                    currentBlink = false;
                    break;
                case 27:
                    currentInverse = false;
                    break;
                case 28:
                    currentInvisible = false;
                    break;
                case 29:
                    currentStrikethrough = false;
                    break;
                case >= 30 and <= 37:
                    currentFg = theme.GetColor(p - 30);
                    break;
                case 38:
                    g = ParseExtendedColor(groups, g, ColorTarget.Foreground);
                    break;
                case 39:
                    currentFg = theme.Foreground;
                    break;
                case >= 40 and <= 47:
                    currentBg = theme.GetColor(p - 40);
                    break;
                case 48:
                    g = ParseExtendedColor(groups, g, ColorTarget.Background);
                    break;
                case 49:
                    currentBg = theme.Background;
                    break;
                case 53:
                    currentOverline = true;
                    break;
                case 55:
                    currentOverline = false;
                    break;
                case 58:
                    g = ParseExtendedColor(groups, g, ColorTarget.Underline);
                    break;
                case 59:
                    // 0 is the sentinel meaning "inherit the foreground color".
                    currentUnderlineColor = 0;
                    break;
                case >= 90 and <= 97:
                    currentFg = theme.GetColor(p - 90 + 8);
                    break;
                case >= 100 and <= 107:
                    currentBg = theme.GetColor(p - 100 + 8);
                    break;
            }
        }
    }

    void ResetStyles()
    {
        currentFg = theme.Foreground;
        currentBg = theme.Background;
        currentBold = false;
        currentFaint = false;
        currentItalic = false;
        currentUnderline = UnderlineStyle.None;
        currentUnderlineColor = 0;
        currentBlink = false;
        currentInverse = false;
        currentInvisible = false;
        currentStrikethrough = false;
        currentOverline = false;
    }

    static UnderlineStyle MapUnderline(int code) =>
        code is >= 0 and <= 5 ? (UnderlineStyle)code : UnderlineStyle.Single;

    void SetColorTarget(ColorTarget target, uint color)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                currentFg = color;
                break;
            case ColorTarget.Background:
                currentBg = color;
                break;
            case ColorTarget.Underline:
                currentUnderlineColor = color;
                break;
        }
    }

    static uint MakeRgb(int r, int g, int b) =>
        0xFF000000u | ((uint)(byte)r << 16) | ((uint)(byte)g << 8) | (byte)b;

    /// <summary>
    /// Parses an extended-color attribute (38/48/58) at group index <paramref name="g"/>,
    /// supporting both the colon form (ESC[38:2:r:g:b], all in one group) and the
    /// legacy semicolon form (ESC[38;2;r;g;b], spread across groups). Returns the
    /// index of the last group consumed.
    /// </summary>
    int ParseExtendedColor(List<List<int>> groups, int g, ColorTarget target)
    {
        var group = groups[g];

        // Colon form: mode and color components are sub-parameters of this group.
        if (group.Count > 1)
        {
            var mode = group[1];
            if (mode == 5 && group.Count >= 3)
            {
                SetColorTarget(target, theme.GetColor(group[2]));
            }
            else if (mode == 2 && group.Count >= 5)
            {
                // The ITU form ESC[38:2::r:g:b carries a colorspace id at index 2,
                // so the rgb triple starts at 3 when 6+ components are present.
                var baseIdx = group.Count >= 6 ? 3 : 2;
                SetColorTarget(
                    target,
                    MakeRgb(group[baseIdx], group[baseIdx + 1], group[baseIdx + 2])
                );
            }
            return g;
        }

        // Semicolon form: mode and components are the following groups.
        if (g + 1 >= groups.Count)
        {
            return g;
        }
        var nextMode = groups[g + 1][0];
        if (nextMode == 5 && g + 2 < groups.Count)
        {
            SetColorTarget(target, theme.GetColor(groups[g + 2][0]));
            return g + 2;
        }
        if (nextMode == 2 && g + 4 < groups.Count)
        {
            SetColorTarget(target, MakeRgb(groups[g + 2][0], groups[g + 3][0], groups[g + 4][0]));
            return g + 4;
        }
        return g + 1;
    }
}
