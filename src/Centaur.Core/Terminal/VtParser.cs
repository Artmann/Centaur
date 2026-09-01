namespace Centaur.Core.Terminal;

public class VtParser
{
    readonly ScreenBuffer mainBuffer;
    readonly ScreenBuffer alternateBuffer;
    ScreenBuffer buffer;
    readonly OscHandler osc;

    // Colours and text styles SGR selects, and the cell the erase operations fill with.
    readonly SgrPen pen;

    // DECSC/DECRC registers, one per screen.
    readonly CursorRegisters cursors;

    // The theme every default colour resolves from, and the cell the erase operations fill
    // with. Both move when ApplyTheme swaps the theme under a running pane.
    TerminalTheme theme;
    Cell blank;

    // DEC private mode state, all of it held by DecModes except the alternate-screen
    // flag, which tracks which of the two buffers this parser is writing to.
    readonly DecModes modes = new();

    /// <summary>Every DEC private mode a program has set: cursor visibility, application
    /// cursor keys, bracketed paste, and the mouse reporting modes.</summary>
    public DecModes Modes => modes;

    public bool IsAlternateScreen { get; private set; }
    public ScreenBuffer ActiveBuffer => buffer;

    // The DA/DSR/DECRQM/XTVERSION replies, and the channel they go out on.
    readonly DeviceReports reports = new();

    /// <summary>What the terminal reports about itself, and the raw response channel back to
    /// the pty that those replies - and OSC colour/clipboard reads - are written to.</summary>
    public DeviceReports Reports => reports;

    /// <summary>State the OSC sequences carry - window title, working directory, the palette
    /// and default colours, the last exit code - and the OSC 52 clipboard event.</summary>
    public OscHandler Osc => osc;

    // The byte-level state machine; this parser only acts on whole sequences.
    readonly VtTokenizer tokenizer = new();

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
        osc = new OscHandler(theme, pen, reports.Reply);
        cursors = new CursorRegisters(alternateBuffer, pen);
    }

    /// <summary>
    /// Adopts a new theme across everything that resolved a colour from the old one: the pen,
    /// the blank cell, the OSC defaults and palette, and the cells already on both screens.
    /// Callers hold the buffer lock, because this rewrites the grid.
    /// </summary>
    public void ApplyTheme(TerminalTheme next)
    {
        var previous = theme;
        if (ReferenceEquals(previous, next))
        {
            return;
        }

        theme = next;
        blank = new Cell(' ', next.Foreground, next.Background);
        pen.ApplyTheme(previous, next);
        osc.ApplyTheme(next);
        mainBuffer.ApplyTheme(previous, next);
        alternateBuffer.ApplyTheme(previous, next);
    }

    /// <summary>Raised for every BEL (0x07) the program emits. What that should do - nothing,
    /// a system sound, a flash - is the user's choice, so the parser only reports it.</summary>
    public event Action? Bell;

    /// <summary>Changes how many scrolled-off rows the main screen keeps.</summary>
    public void SetScrollbackLines(int lines) => mainBuffer.Scrollback.Resize(lines);

    public void Resize(int columns, int rows)
    {
        mainBuffer.Resize(columns, rows);
        alternateBuffer.Resize(columns, rows);
    }

    public void Process(ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            switch (tokenizer.Feed(b))
            {
                case VtToken.Print:
                    foreach (var c in tokenizer.Text)
                    {
                        ScreenOps.Write(buffer, pen.Paint(c));
                    }
                    break;
                case VtToken.Control:
                    if (tokenizer.Code == 0x07)
                    {
                        Bell?.Invoke();
                    }
                    _ = ScreenCommands.TryExecuteControl(buffer, tokenizer.Code);
                    break;
                case VtToken.Escape:
                    ExecuteEscape((char)tokenizer.Code);
                    break;
                case VtToken.Csi:
                    ExecuteCsi((char)tokenizer.Code);
                    break;
                case VtToken.Osc:
                    osc.Dispatch(tokenizer.OscPayload, buffer);
                    break;
            }
        }
    }

    /// <summary>Acts on a two-byte escape. ESC [ and ESC ] open longer sequences and never
    /// reach here; the tokenizer has already turned them into a CSI or OSC token.</summary>
    void ExecuteEscape(char final)
    {
        switch (final)
        {
            case 'D': // IND - Index (move down)
                ScreenOps.LineFeed(buffer);
                break;
            case 'E': // NEL - Next Line
                buffer.cursorX = 0;
                ScreenOps.LineFeed(buffer);
                break;
            case 'M': // RI - Reverse Index (move up)
                ScreenOps.ReverseIndex(buffer);
                break;
            case '7': // DECSC - Save cursor
                cursors.Save(buffer);
                break;
            case '8': // DECRC - Restore cursor
                cursors.Restore(buffer);
                break;
        }
    }

    void ExecuteCsi(char command)
    {
        // Private/prefixed CSI ( '<' '=' '>' '?' ) must not fall through to the ANSI
        // cursor/SGR handlers. Kitty-keyboard 'CSI > u' / 'CSI < u' / 'CSI = u' and
        // XTMODKEYS 'CSI > m' would otherwise hijack RCP/SGR and move the cursor.
        var csi = tokenizer.Csi;
        if (csi.Prefix != '\0')
        {
            ExecutePrivateCsi(command, csi);
            return;
        }

        if (!ScreenCommands.TryExecuteCsi(buffer, command, csi.Args, blank))
        {
            ExecuteAnsiCsi(command, csi);
        }
    }

    // The CSI commands that need more than the screen: the pen, the reply channel and the
    // saved-cursor registers.
    void ExecuteAnsiCsi(char command, CsiSequence csi)
    {
        var args = csi.Args;
        switch (command)
        {
            case 'S': // SU - Scroll Up
                buffer.Region.ScrollUp(args.Get(0));
                break;
            case 'T': // SD - Scroll Down
                buffer.Region.ScrollDown(args.Get(0));
                break;
            case 'm': // SGR - Select Graphic Rendition
                pen.Apply(csi.Values, csi.IsColon);
                break;
            case 'c': // DA1 - primary Device Attributes (unprefixed)
                reports.DeviceAttributes(csi.Prefix);
                break;
            case 'n': // DSR - Device Status Report
                reports.DeviceStatus(args.Get(0, 0), buffer);
                break;
            case 's': // SCP - Save Cursor Position (ANSI)
                cursors.Save(buffer);
                break;
            case 'u': // RCP - Restore Cursor Position (ANSI)
                cursors.Restore(buffer);
                break;
            case 'r': // DECSTBM - Set Top and Bottom Margins, 1-based
                buffer.Region.Set(args.Get(0) - 1, args.Get(1, buffer.rows) - 1);
                buffer.cursorX = 0;
                buffer.cursorY = 0;
                break;
        }
    }

    // Dispatch a CSI sequence that carried a private prefix ('<' '=' '>' '?').
    // Only the prefix-aware commands act; everything else (notably Kitty-keyboard
    // 'u', XTMODKEYS 'm', DSR 'n', prefixed 's') is ignored so it cannot reach the
    // ANSI cursor/SGR handlers.
    void ExecutePrivateCsi(char command, CsiSequence csi)
    {
        switch (command)
        {
            case 'c': // DA2 ('>') / DA3 ('=')
                reports.DeviceAttributes(csi.Prefix);
                break;
            case 'h': // SM - Set Mode (DEC private)
            case 'l': // RM - Reset Mode (DEC private)
                if (csi.Prefix == '?')
                {
                    ExecuteDecMode(command, csi.Values);
                }
                break;
            case 'p': // DECRQM - Request Mode (CSI ? Ps $ p)
                ReportMode(csi);
                break;
            case 'q': // XTVERSION - report terminal name/version (CSI > q)
                reports.Version(csi.Prefix, csi.Intermediate);
                break;
        }
    }

    void ExecuteDecMode(char command, List<int> requested)
    {
        var enabled = command == 'h';
        foreach (var mode in requested)
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
            cursors.Save(buffer);
            buffer = alternateBuffer;
            buffer.Clear();
            buffer.Region.Set(0, buffer.rows - 1);
        }
        else
        {
            // Switch back to main, then restore from the main register. The app's
            // save/restore on the alternate screen used altSaved, so the main-screen
            // cursor saved on 1049h is intact.
            buffer = mainBuffer;
            cursors.Restore(buffer);
        }
        IsAlternateScreen = enabled;
    }

    /// <summary>DECRQM (CSI ? Ps $ p): answer with how DecModes currently reports the mode.</summary>
    void ReportMode(CsiSequence csi)
    {
        if (csi.Prefix != '?' || csi.Intermediate != '$')
        {
            return;
        }

        var mode = csi.Values.Count > 0 ? csi.Values[0] : 0;
        reports.ModeSetting(mode, modes.Report(mode, IsAlternateScreen));
    }
}
