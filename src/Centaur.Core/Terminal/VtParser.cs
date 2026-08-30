using System.Reflection;
using System.Runtime.InteropServices;

namespace Centaur.Core.Terminal;

public class VtParser
{
    readonly ScreenBuffer mainBuffer;
    readonly ScreenBuffer alternateBuffer;
    ScreenBuffer buffer;
    readonly OscHandler osc;

    // Colours and text styles SGR selects, and the cell the erase operations fill with.
    readonly SgrPen pen;
    readonly Cell blank;

    // DEC private mode state, all of it held by DecModes except the alternate-screen
    // flag, which tracks which of the two buffers this parser is writing to.
    readonly DecModes modes = new();
    public bool CursorVisible => modes.CursorVisible;
    public bool ApplicationCursorKeys => modes.ApplicationCursorKeys;
    public bool BracketedPasteMode => modes.BracketedPasteMode;
    public bool IsAlternateScreen { get; private set; }

    // Mouse reporting modes.
    public MouseTrackingMode MouseTracking => modes.MouseTracking; // 1000/1002/1003
    public bool MouseSgrMode => modes.MouseSgrMode; // 1006
    public bool FocusEventMode => modes.FocusEventMode; // 1004
    public bool AltScrollMode => modes.AltScrollMode; // 1007
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
    public string? WindowTitle => osc.WindowTitle; // OSC 0/2
    public string? IconName => osc.IconName; // OSC 0/1
    public string? WorkingDirectory => osc.WorkingDirectory; // OSC 7
    public uint[] Palette => osc.Palette; // OSC 4/104
    public uint DefaultForeground => osc.DefaultForeground; // OSC 10
    public uint DefaultBackground => osc.DefaultBackground; // OSC 11
    public int? LastExitCode => osc.LastExitCode; // OSC 133;D;<code>

    // Fired for OSC 52 clipboard writes/clears (read requests use Respond instead).
    public event Action<ClipboardRequest>? ClipboardChanged
    {
        add => osc.ClipboardChanged += value;
        remove => osc.ClipboardChanged -= value;
    }

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
    readonly Utf8Decoder utf8 = new();

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
        pen = new SgrPen(theme);
        blank = new Cell(' ', theme.Foreground, theme.Background);
        osc = new OscHandler(theme, pen, Reply);
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
                    osc.Dispatch(CollectionsMarshal.AsSpan(oscBuffer), buffer);
                }
                state = State.Ground;
                break;
        }
    }

    void ProcessGround(byte b)
    {
        if (b == 0x1B) // ESC
        {
            state = State.Escape;
            return;
        }
        if (ScreenCommands.TryExecuteControl(buffer, b))
        {
            return;
        }

        if (utf8.TryDecode(b, out var text))
        {
            foreach (var c in text)
            {
                ScreenOps.Write(buffer, pen.Paint(c));
            }
        }
        else if (b >= 0x20) // ASCII printable
        {
            ScreenOps.Write(buffer, pen.Paint((char)b));
        }
    }

    void ProcessEscape(byte b)
    {
        // Every escape ends here unless it opens a longer sequence.
        state = State.Ground;
        switch (b)
        {
            case (byte)'[': // CSI
                BeginCsi();
                break;
            case (byte)'D': // IND - Index (move down)
                ScreenOps.LineFeed(buffer);
                break;
            case (byte)'E': // NEL - Next Line
                buffer.cursorX = 0;
                ScreenOps.LineFeed(buffer);
                break;
            case (byte)'M': // RI - Reverse Index (move up)
                ScreenOps.ReverseIndex(buffer);
                break;
            case (byte)']': // OSC - Operating System Command
                state = State.Osc;
                oscBuffer.Clear();
                break;
            case (byte)'7': // DECSC - Save cursor
                SaveCursor();
                break;
            case (byte)'8': // DECRC - Restore cursor
                RestoreCursor();
                break;
        }
    }

    void BeginCsi()
    {
        state = State.Csi;
        csiParams.Clear();
        csiParamIsColon.Clear();
        pendingColon = false;
        currentParam = 0;
        csiPrefix = '\0';
        csiIntermediate = '\0';
    }

    void ProcessCsi(byte b)
    {
        if (TryAccumulateParam(b))
        {
            state = State.CsiParam;
            return;
        }

        if (b >= 0x40 && b <= 0x7E)
        {
            // Final byte - execute command
            PushParam();
            ExecuteCsi((char)b);
        }
        // Either the sequence just ran or the byte was junk; both end it.
        state = State.Ground;
    }

    /// <summary>Consumes everything a CSI sequence can carry ahead of its final byte: the
    /// digits, the two separators, the private prefix and the intermediate byte.</summary>
    bool TryAccumulateParam(byte b)
    {
        if (b >= '0' && b <= '9')
        {
            currentParam = currentParam * 10 + (b - '0');
            return true;
        }
        if (b == ';')
        {
            PushParam();
            pendingColon = false;
            return true;
        }
        if (b == ':')
        {
            // Colon sub-parameter: the next param belongs to this param's group.
            PushParam();
            pendingColon = true;
            return true;
        }
        if (b >= 0x3C && b <= 0x3F)
        {
            // Private parameter prefix: '<' '=' '>' '?'
            csiPrefix = (char)b;
            return true;
        }
        if (b >= 0x20 && b <= 0x2F)
        {
            // Intermediate byte (e.g. '$' in DECRQM's CSI ? Ps $ p).
            csiIntermediate = (char)b;
            return true;
        }
        return false;
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

        var args = new CsiArgs(csiParams);
        if (!ScreenCommands.TryExecuteCsi(buffer, command, args, blank))
        {
            ExecuteAnsiCsi(command, args);
        }
    }

    // The CSI commands that need more than the screen: the pen, the reply channel and the
    // saved-cursor registers.
    void ExecuteAnsiCsi(char command, CsiArgs args)
    {
        switch (command)
        {
            case 'S': // SU - Scroll Up
                buffer.ScrollUp(args.Get(0));
                break;
            case 'T': // SD - Scroll Down
                buffer.ScrollDown(args.Get(0));
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
            case 'r': // DECSTBM - Set Top and Bottom Margins, 1-based
                buffer.SetScrollRegion(args.Get(0) - 1, args.Get(1, buffer.rows) - 1);
                buffer.cursorX = 0;
                buffer.cursorY = 0;
                break;
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
                HandleDecrqm();
                break;
            case 'q': // XTVERSION - report terminal name/version (CSI > q)
                HandleXtversion();
                break;
        }
    }

    void HandleXtversion()
    {
        if (csiPrefix == '>' && csiIntermediate == '\0')
        {
            Reply($"\x1bP>|Centaur({TerminalVersion})\x1b\\");
        }
    }

    void ExecuteDecMode(char command)
    {
        var enabled = command == 'h';
        foreach (var mode in csiParams)
        {
            if (!modes.TrySet(mode, enabled) && mode == 1049)
            {
                SetAlternateScreen(enabled);
            }
        }
    }

    void SetAlternateScreen(bool enabled)
    {
        if (enabled)
        {
            // Save the main-screen cursor (into the main register, since the main buffer
            // is still active), switch to alternate, clear.
            SaveCursor();
            buffer = alternateBuffer;
            buffer.Clear();
            buffer.SetScrollRegion(0, buffer.rows - 1);
        }
        else
        {
            // Switch back to main, then restore from the main register. The app's
            // save/restore on the alternate screen used altSaved, so the main-screen
            // cursor saved on 1049h is intact.
            buffer = mainBuffer;
            RestoreCursor();
        }
        IsAlternateScreen = enabled;
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
        if (csiPrefix != '?' || csiIntermediate != '$')
        {
            return;
        }

        var mode = csiParams.Count > 0 ? csiParams[0] : 0;
        Reply($"[?{mode};{modes.Report(mode, IsAlternateScreen)}$y");
    }

    void ProcessOsc(byte b)
    {
        if (b == 0x07)
        {
            // BEL terminates OSC
            osc.Dispatch(CollectionsMarshal.AsSpan(oscBuffer), buffer);
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
}
