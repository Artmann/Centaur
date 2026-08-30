using System.Reflection;

namespace Centaur.Core.Terminal;

public class VtParser
{
    readonly ScreenBuffer mainBuffer;
    readonly ScreenBuffer alternateBuffer;
    ScreenBuffer buffer;
    readonly TerminalTheme theme;

    // Colours and text styles SGR selects, and the cell the erase operations fill with.
    readonly SgrPen pen;
    readonly Cell blank;

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
    public static string TerminalVersion { get; } = ResolveVersion();

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
        slot.fg = pen.Foreground;
        slot.bg = pen.Background;
    }

    void RestoreCursor()
    {
        ref var slot = ref CurrentSaved();
        buffer.cursorX = slot.x;
        buffer.cursorY = slot.y;
        pen.Foreground = slot.fg;
        pen.Background = slot.bg;
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
        pen = new SgrPen(theme);
        blank = new Cell(' ', theme.Foreground, theme.Background);
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
                ScreenOps.LineFeed(buffer);
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
                    ScreenOps.Write(buffer, pen.Paint((char)b));
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
                ScreenOps.LineFeed(buffer);
                state = State.Ground;
                break;
            case (byte)'E': // NEL - Next Line
                buffer.cursorX = 0;
                ScreenOps.LineFeed(buffer);
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
                ScreenOps.EraseInDisplay(buffer, Param(0, 0), blank);
                break;
            case 'K': // EL - Erase in Line
                ScreenOps.EraseInLine(buffer, Param(0, 0), blank);
                break;
            case 'L': // IL - Insert Lines
                ScreenOps.InsertLines(buffer, Param(0), blank);
                break;
            case 'M': // DL - Delete Lines
                ScreenOps.DeleteLines(buffer, Param(0), blank);
                break;
            case 'P': // DCH - Delete Characters
                ScreenOps.DeleteCharacters(buffer, Param(0), blank);
                break;
            case '@': // ICH - Insert Characters
                ScreenOps.InsertCharacters(buffer, Param(0), blank);
                break;
            case 'X': // ECH - Erase Characters
                ScreenOps.EraseCharacters(buffer, Param(0), blank);
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
                pen.Apply(csiParams, csiParamIsColon);
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
                    Reply($"\x1bP>|Centaur({TerminalVersion})\x1b\\");
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
            ScreenOps.Write(buffer, pen.Paint(chars[i]));
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
        pen.Hyperlink = uri.Length > 0 ? uri : null;
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
}
