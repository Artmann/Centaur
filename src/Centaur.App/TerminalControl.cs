using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using Centaur.App.Menus;
using Centaur.App.Splits;
using Centaur.Core.Hosting;
using Centaur.Core.Pty;
using Centaur.Core.Terminal;
using Centaur.Pty.Windows;
using Centaur.Rendering;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Centaur.App;

public class TerminalControl : Control, IPaneTerminal
{
    Control IPaneTerminal.View => this;

    bool IPaneTerminal.Focus() => Focus();

    readonly ExtensionHost host;
    readonly INotificationService notifications;
    readonly TerminalTheme theme;
    readonly ScreenBuffer initialBuffer;
    readonly TerminalRenderer renderer;
    readonly RenderProfiler profiler;
    readonly FpsOverlayExtension fpsOverlay;
    readonly FrameScheduler scheduler = new();
    readonly VtParser parser;
    readonly object bufferLock = new();

    // Serializes all writes to the PTY input pipe. SendToPty (UI thread) and
    // RespondToPty (PTY read thread) can fire concurrently; without this their
    // async write/flush pairs could interleave and corrupt the byte stream.
    readonly SemaphoreSlim ptyWriteLock = new(1, 1);

    ConPtyConnection? pty;
    CancellationTokenSource? readCts;
    Task? readTask;
    bool isAttached;
    bool ptyStarted;

    // Suggestion state
    readonly SuggestionState suggestionState;
    ISuggestionProvider? suggestionProvider;
    int promptEndCol;
    bool awaitingPrompt = true;

    // Selection state (UI thread only)
    int selAnchorCol,
        selAnchorRow;
    int selCurrentCol,
        selCurrentRow;
    bool isDragging;
    bool hasSelection;
    int selectionMode; // 0=char, 1=word, 2=line
    int wordAnchorStart,
        wordAnchorEnd; // word-mode anchor boundaries

    // Reverse search state
    readonly CommandHistory commandHistory;
    readonly ReverseSearchState reverseSearchState;
    ReverseSearchOverlay? reverseSearchOverlay;
    bool reverseSearchActive;

    // Settings state
    readonly Settings settings;
    SettingsOverlay? settingsOverlay;
    bool settingsActive;

    // Read-only state (per-pane)
    bool isReadOnly;

    public TerminalControl()
    {
        host = App.Services.GetRequiredService<ExtensionHost>();
        notifications = App.Services.GetRequiredService<INotificationService>();
        suggestionState = App.Services.GetRequiredService<SuggestionState>();
        commandHistory = App.Services.GetRequiredService<CommandHistory>();
        reverseSearchState = App.Services.GetRequiredService<ReverseSearchState>();
        settings = App.Services.GetRequiredService<Settings>();

        var themeProvider = host.GetProvider<IThemeProvider>();
        theme =
            themeProvider?.GetThemes().FirstOrDefault(t => t.Id == "catppuccin-macchiato")?.Theme
            ?? CatppuccinThemes.Macchiato;

        profiler = App.Services.GetRequiredService<RenderProfiler>();
        fpsOverlay = App.Services.GetRequiredService<FpsOverlayExtension>();
        renderer = new TerminalRenderer(theme, profiler: profiler);

        // Start with a default size; will resize once we know actual bounds
        initialBuffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(initialBuffer, theme);

        // Forward terminal query replies (Device Attributes, DECRQM, OSC color/clipboard)
        // back to the child process. Without this, capability probes from TUIs such as
        // Claude Code never get answered, so the app stalls on its startup timeouts before
        // it will echo input.
        parser.Respond += RespondToPty;

        Focusable = true;
        ClipToBounds = true;

        ContextMenu = BuildContextMenu();
    }

    public event Action<SplitDirection>? SplitRequested;
    public event Action? CloseRequested;

    ContextMenu BuildContextMenu()
    {
        var menu = new ContextMenu();
        var context = new MenuContext(this);

        menu.Opening += (_, _) =>
        {
            menu.Items.Clear();

            var items = host.GetProviders<ITerminalContextMenuProvider>()
                .SelectMany(p => p.GetItems(context))
                .Where(i => i.IsVisible)
                .ToList();

            string? lastGroup = null;
            foreach (var item in items)
            {
                if (lastGroup != null && item.Group != lastGroup)
                {
                    menu.Items.Add(new Separator());
                }

                var menuItem = new MenuItem { Header = item.Label };
                if (item.IsChecked.HasValue)
                {
                    menuItem.ToggleType = MenuItemToggleType.CheckBox;
                    menuItem.IsChecked = item.IsChecked.Value;
                }

                var onInvoke = item.OnInvoke;
                menuItem.Click += (_, _) => onInvoke();

                menu.Items.Add(menuItem);
                lastGroup = item.Group;
            }
        };

        return menu;
    }

    sealed class MenuContext : ITerminalContextMenuContext
    {
        readonly TerminalControl owner;

        public MenuContext(TerminalControl owner)
        {
            this.owner = owner;
        }

        public bool HasSelection => owner.hasSelection;
        public bool IsReadOnly => owner.isReadOnly;

        public void ToggleReadOnly()
        {
            owner.isReadOnly = !owner.isReadOnly;
            owner.MarkDirty();
        }

        public void Copy() => owner.CopySelectionToClipboard();

        public void Paste() => owner.PasteFromClipboard();

        public void Split(SplitDirection direction) => owner.SplitRequested?.Invoke(direction);

        public void Close() => owner.CloseRequested?.Invoke();
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);
        UpdateGridSize(finalSize.Width, finalSize.Height);
        return result;
    }

    void UpdateGridSize(double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var newCols = Math.Max(1, (int)(width / renderer.cellWidth));
        var newRows = Math.Max(1, (int)(height / renderer.cellHeight));

        if (newCols == parser.ActiveBuffer.columns && newRows == parser.ActiveBuffer.rows)
        {
            if (!ptyStarted && isAttached)
            {
                ptyStarted = true;
                StartPty();
            }
            return;
        }

        lock (bufferLock)
        {
            parser.Resize(newCols, newRows);
        }

        MarkDirty();

        if (!ptyStarted && isAttached)
        {
            ptyStarted = true;
            StartPty();
        }
        else
        {
            pty?.Resize(newCols, newRows);
        }
    }

    public event Action? PtyExited;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        suggestionProvider = host.GetProvider<ISuggestionProvider>();
        isAttached = true;
        MarkDirty();
        ScheduleFrame();
        // PTY start is deferred until ArrangeOverride provides the real size
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Pause the animation loop. PTY and renderer survive detach so the control
        // can be re-parented (e.g. when a tab is split into panes) without losing state.
        // Final teardown happens in Close().
        isAttached = false;
        base.OnDetachedFromVisualTree(e);
    }

    bool closed;

    public void Close()
    {
        if (closed)
        {
            return;
        }
        closed = true;
        StopPty();
        renderer.Dispose();
    }

    void ScheduleFrame()
    {
        if (!isAttached)
        {
            return;
        }

        TopLevel.GetTopLevel(this)?.RequestAnimationFrame(OnFrame);
    }

    void OnFrame(TimeSpan timestamp)
    {
        if (!isAttached)
        {
            return;
        }

        var overlaysSelfUpdating = fpsOverlay.Enabled || profiler.Enabled;
        if (scheduler.Tick(Stopwatch.GetTimestamp(), overlaysSelfUpdating))
        {
            InvalidateVisual();
        }
        ScheduleFrame();
    }

    // Single entry point for "something visible changed; redraw on the next vsync". Safe to
    // call from any thread (the PTY read thread is the main off-thread caller).
    void MarkDirty() => scheduler.MarkDirty();

    async void StartPty()
    {
        try
        {
            int cols,
                rows;
            lock (bufferLock)
            {
                cols = parser.ActiveBuffer.columns;
                rows = parser.ActiveBuffer.rows;
            }

            var workingDirectory = settings.GetStartingDirectory();
            if (workingDirectory != null && !Directory.Exists(workingDirectory))
            {
                notifications.Show(
                    "Starting Directory",
                    $"Directory \"{workingDirectory}\" not found. Using default instead.",
                    NotificationSeverity.Warning
                );
                workingDirectory = null;
            }

            var options = new PtyOptions(
                executable: "powershell.exe",
                columns: cols,
                rows: rows,
                workingDirectory: workingDirectory
            );

            pty = await ConPtyConnection.CreateAsync(options);
            readCts = new CancellationTokenSource();
            readTask = Task.Run(() => ReadLoopAsync(readCts.Token));
        }
        catch (Exception ex)
        {
            notifications.Show("PTY Error", ex.Message, NotificationSeverity.Error);
        }
    }

    async void StopPty()
    {
        readCts?.Cancel();
        if (readTask != null)
        {
            try
            {
                await readTask;
            }
            catch { }
        }
        if (pty != null)
        {
            await pty.DisposeAsync();
            pty = null;
        }
        readCts?.Dispose();
        readCts = null;
    }

    async Task ReadLoopAsync(CancellationToken ct)
    {
        if (pty == null)
            return;

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await pty.Output.ReadAsync(ct);
                var ptyBuffer = result.Buffer;

                lock (bufferLock)
                {
                    foreach (var segment in ptyBuffer)
                    {
                        parser.Process(segment.Span);
                    }

                    if (awaitingPrompt)
                    {
                        promptEndCol = parser.ActiveBuffer.cursorX;
                    }
                }

                // PTY bytes can change anything visible — buffer contents, cursor visibility
                // (DECTCEM), alt-screen swap, scrollback. One flag covers them all.
                MarkDirty();

                pty.Output.AdvanceTo(ptyBuffer.End);

                if (result.IsCompleted)
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            // Fall through to fire PtyExited
        }

        Dispatcher.UIThread.Post(() => PtyExited?.Invoke());
    }

    (int col, int row) PixelToGrid(Point p)
    {
        var active = parser.ActiveBuffer;
        var col = Math.Clamp((int)(p.X / renderer.cellWidth), 0, active.columns - 1);
        var row = Math.Clamp((int)(p.Y / renderer.cellHeight), 0, active.rows - 1);
        return (col, row);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);
        if (!point.Properties.IsLeftButtonPressed)
        {
            // Right-click (and middle-click) bypass selection so the context menu can open
            // and so this control still receives focus for pane-focus tracking.
            Focus();
            return;
        }

        var (col, row) = PixelToGrid(point.Position);
        var clickCount = e.ClickCount;

        if (clickCount >= 3)
        {
            // Triple-click: select entire line
            selectionMode = 2;
            selAnchorCol = 0;
            selAnchorRow = row;
            selCurrentCol = parser.ActiveBuffer.columns;
            selCurrentRow = row;
            hasSelection = true;
        }
        else if (clickCount == 2)
        {
            // Double-click: select word
            selectionMode = 1;
            lock (bufferLock)
            {
                wordAnchorStart = TextSelection.FindWordStart(parser.ActiveBuffer, col, row);
                wordAnchorEnd = TextSelection.FindWordEnd(parser.ActiveBuffer, col, row);
            }
            selAnchorCol = wordAnchorStart;
            selAnchorRow = row;
            selCurrentCol = wordAnchorEnd;
            selCurrentRow = row;
            hasSelection = true;
        }
        else
        {
            // Single click: character selection
            selectionMode = 0;
            selAnchorCol = col;
            selAnchorRow = row;
            selCurrentCol = col;
            selCurrentRow = row;
            hasSelection = false;
        }

        isDragging = true;

        MarkDirty();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!isDragging)
            return;

        var (col, row) = PixelToGrid(e.GetPosition(this));

        if (selectionMode == 2)
        {
            // Line mode: snap to full lines
            if (row < selAnchorRow)
            {
                selAnchorCol = parser.ActiveBuffer.columns;
                selCurrentCol = 0;
                selCurrentRow = row;
            }
            else
            {
                selAnchorCol = 0;
                selCurrentCol = parser.ActiveBuffer.columns;
                selCurrentRow = row;
            }
            hasSelection = true;
        }
        else if (selectionMode == 1)
        {
            // Word mode: snap to word boundaries
            lock (bufferLock)
            {
                bool beforeAnchor =
                    row < selAnchorRow || (row == selAnchorRow && col < wordAnchorStart);
                if (beforeAnchor)
                {
                    selAnchorCol = wordAnchorEnd;
                    selCurrentCol = TextSelection.FindWordStart(parser.ActiveBuffer, col, row);
                    selCurrentRow = row;
                }
                else
                {
                    selAnchorCol = wordAnchorStart;
                    selCurrentCol = TextSelection.FindWordEnd(parser.ActiveBuffer, col, row);
                    selCurrentRow = row;
                }
            }
            hasSelection = true;
        }
        else
        {
            // Char mode
            selCurrentCol = col;
            selCurrentRow = row;

            if (col != selAnchorCol || row != selAnchorRow)
                hasSelection = true;
        }

        MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!isDragging)
            return;

        isDragging = false;
        e.Pointer.Capture(null);

        var (col, row) = PixelToGrid(e.GetPosition(this));
        if (selectionMode == 0 && col == selAnchorCol && row == selAnchorRow)
            hasSelection = false;

        MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (parser.IsAlternateScreen)
        {
            return;
        }

        var delta = (int)e.Delta.Y;
        var scrollLines = Math.Max(1, Math.Abs(delta) * 3);

        lock (bufferLock)
        {
            if (delta > 0)
            {
                parser.ActiveBuffer.ScrollViewUp(scrollLines);
            }
            else
            {
                parser.ActiveBuffer.ScrollViewDown(scrollLines);
            }
        }

        hasSelection = false;
        MarkDirty();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (reverseSearchActive || settingsActive)
        {
            return;
        }

        if (pty == null)
        {
            return;
        }

        byte[]? bytes = null;

        // Shift+PageUp/PageDown for scrollback navigation
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && !parser.IsAlternateScreen)
        {
            if (e.Key == Key.PageUp)
            {
                lock (bufferLock)
                {
                    parser.ActiveBuffer.ScrollViewUp(parser.ActiveBuffer.rows - 1);
                }
                hasSelection = false;
                MarkDirty();
                e.Handled = true;
                return;
            }
            if (e.Key == Key.PageDown)
            {
                lock (bufferLock)
                {
                    parser.ActiveBuffer.ScrollViewDown(parser.ActiveBuffer.rows - 1);
                }
                hasSelection = false;
                MarkDirty();
                e.Handled = true;
                return;
            }
        }

        // Shift+Insert paste
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.Insert)
        {
            PasteFromClipboard();
            e.Handled = true;
            return;
        }

        // Accept suggestion with Tab (no modifiers)
        if (e.Key == Key.Tab && e.KeyModifiers == KeyModifiers.None)
        {
            var (ghost, _, _) = suggestionState.Read();
            if (!string.IsNullOrEmpty(ghost))
            {
                SendToPty(Encoding.UTF8.GetBytes(ghost));
                suggestionState.Clear();
                e.Handled = true;
                return;
            }
        }

        // Toggle the render profiler (checked before the generic Ctrl block so the
        // Ctrl+letter -> control-byte conversion below doesn't swallow it).
        if (
            e.KeyModifiers.HasFlag(KeyModifiers.Control)
            && e.KeyModifiers.HasFlag(KeyModifiers.Shift)
            && e.Key == Key.P
        )
        {
            profiler.Enabled = !profiler.Enabled;
            // Profiler overlay visibility just toggled; also flips the heartbeat policy.
            MarkDirty();
            notifications.Show(
                "Render Profiler",
                profiler.Enabled
                    ? "Profiling ON — overlay + console dump every 2s. Ctrl+Shift+P to stop."
                    : "Profiling OFF — final summary written to console.",
                NotificationSeverity.Info
            );
            e.Handled = true;
            return;
        }

        // Handle Ctrl+key combinations
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            if (e.Key == Key.C && hasSelection)
            {
                CopySelectionToClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.V)
            {
                PasteFromClipboard();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.R)
            {
                OpenReverseSearch();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.OemComma)
            {
                OpenSettings();
                e.Handled = true;
                return;
            }

            if (e.Key >= Key.A && e.Key <= Key.Z)
            {
                // Ctrl+A = 0x01, Ctrl+C (no selection) = 0x03, etc.
                bytes = new byte[] { (byte)(e.Key - Key.A + 1) };
                suggestionState.Clear();
            }
        }
        else
        {
            // Capture command on Enter before sending to PTY
            if (e.Key == Key.Enter && !isReadOnly)
            {
                var input = ExtractUserInput();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    host.Events.Publish(new CommandSubmittedEvent(input.Trim()));
                    TrackDirectoryChange(input.Trim());
                }
                suggestionState.Clear();
                awaitingPrompt = true;
            }

            // Clear suggestion on navigation/editing keys
            if (
                e.Key
                is Key.Up
                    or Key.Down
                    or Key.Escape
                    or Key.Back
                    or Key.Delete
                    or Key.Left
                    or Key.Home
                    or Key.End
            )
            {
                suggestionState.Clear();
            }

            bytes = TerminalKeyEncoder.Encode(e.Key, e.KeyModifiers);
        }

        if (bytes != null)
        {
            SendToPty(bytes);
            e.Handled = true;
        }
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (pty == null || isReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        awaitingPrompt = false;
        var bytes = Encoding.UTF8.GetBytes(e.Text);
        SendToPty(bytes);
        UpdateSuggestion(e.Text);
        e.Handled = true;
    }

    string ExtractUserInput()
    {
        lock (bufferLock)
        {
            var buf = parser.ActiveBuffer;
            var length = buf.cursorX - promptEndCol;
            if (length <= 0)
            {
                return string.Empty;
            }

            var row = buf.GetRow(buf.cursorY);
            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = row[promptEndCol + i].character;
            }
            return new string(chars).TrimEnd();
        }
    }

    void UpdateSuggestion(string? appendedText = null)
    {
        if (suggestionProvider == null || parser.IsAlternateScreen || awaitingPrompt)
        {
            suggestionState.Clear();
            MarkDirty();
            return;
        }

        var input = ExtractUserInput();
        if (appendedText != null)
        {
            input += appendedText;
        }

        var match = suggestionProvider.GetSuggestion(input);
        if (match != null && match.Length > input.Length)
        {
            var ghost = match.Substring(input.Length);
            int col;
            int row;
            lock (bufferLock)
            {
                col = parser.ActiveBuffer.cursorX;
                row = parser.ActiveBuffer.cursorY;
            }
            // Offset by the appended text length since the echo hasn't arrived yet
            if (appendedText != null)
            {
                col += appendedText.Length;
            }
            suggestionState.Update(ghost, col, row);
        }
        else
        {
            suggestionState.Clear();
        }
        MarkDirty();
    }

    void OpenReverseSearch()
    {
        if (reverseSearchActive)
        {
            return;
        }

        reverseSearchActive = true;

        if (reverseSearchOverlay == null)
        {
            reverseSearchOverlay = new ReverseSearchOverlay(reverseSearchState);
            reverseSearchOverlay.CommandSelected += command =>
            {
                CloseReverseSearch();
                host.Events.Publish(new CommandSubmittedEvent(command));
                SendToPty(Encoding.UTF8.GetBytes(command + "\r"));
            };
            reverseSearchOverlay.CloseRequested += CloseReverseSearch;

            if (Parent is Panel panel)
            {
                panel.Children.Add(reverseSearchOverlay);
            }
        }

        reverseSearchOverlay.Show(theme, commandHistory.GetAll());
        host.Events.Publish(new ReverseSearchRequestedEvent());
    }

    void CloseReverseSearch()
    {
        reverseSearchOverlay?.Hide();
        reverseSearchActive = false;
        Focus();
    }

    void OpenSettings()
    {
        if (settingsActive)
        {
            return;
        }

        settingsActive = true;

        if (settingsOverlay == null)
        {
            settingsOverlay = new SettingsOverlay(settings);
            settingsOverlay.CloseRequested += CloseSettings;

            if (Parent is Panel panel)
            {
                panel.Children.Add(settingsOverlay);
            }
        }

        settingsOverlay.Show(theme);
        host.Events.Publish(new SettingsRequestedEvent());
    }

    void CloseSettings()
    {
        settingsOverlay?.Hide();
        settingsActive = false;
        Focus();
    }

    void TrackDirectoryChange(string command)
    {
        string? targetDir = null;

        foreach (var prefix in new[] { "cd ", "Set-Location ", "pushd ", "chdir ", "sl " })
        {
            if (command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                targetDir = command[prefix.Length..].Trim().Trim('"', '\'');
                break;
            }
        }

        if (targetDir == null)
        {
            return;
        }

        if (
            targetDir == "~"
            || targetDir.StartsWith("~/", StringComparison.Ordinal)
            || targetDir.StartsWith("~\\", StringComparison.Ordinal)
        )
        {
            targetDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                targetDir[1..].TrimStart('/', '\\')
            );
        }

        if (!Path.IsPathRooted(targetDir) && !string.IsNullOrEmpty(settings.LastFolder))
        {
            targetDir = Path.GetFullPath(Path.Combine(settings.LastFolder, targetDir));
        }

        if (Directory.Exists(targetDir))
        {
            settings.UpdateLastFolder(targetDir);
        }
    }

    // Writes parser-generated protocol replies straight to the child process. Unlike
    // SendToPty this bypasses the read-only gate (these are automatic responses to the
    // program's own queries, not user input) and does not scroll the view. Invoked from
    // the read thread inside parser.Process; the write itself targets the input pipe, not
    // the buffer, so it does not contend with bufferLock.
    async void RespondToPty(byte[] data)
    {
        var connection = pty;
        if (connection == null)
        {
            return;
        }

        await ptyWriteLock.WaitAsync();
        try
        {
            await connection.Input.WriteAsync(data);
            await connection.Input.FlushAsync();
        }
        catch (Exception ex)
        {
            notifications.Show("Terminal Error", ex.Message, NotificationSeverity.Error);
        }
        finally
        {
            ptyWriteLock.Release();
        }
    }

    async void SendToPty(byte[] data)
    {
        if (pty == null || isReadOnly)
        {
            return;
        }

        lock (bufferLock)
        {
            parser.ActiveBuffer.ScrollToBottom();
        }
        MarkDirty();

        await ptyWriteLock.WaitAsync();
        try
        {
            await pty.Input.WriteAsync(data);
            await pty.Input.FlushAsync();
        }
        catch (Exception ex)
        {
            notifications.Show("Input Error", ex.Message, NotificationSeverity.Error);
        }
        finally
        {
            ptyWriteLock.Release();
        }
    }

    // TODO: wrap paste in bracketed paste sequences when parser.bracketedPasteMode is true
    async void PasteFromClipboard()
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            return;
        }

        var text = await clipboard.GetTextAsync();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Normalize line endings to \r for the terminal
        text = text.Replace("\r\n", "\r").Replace("\n", "\r");

        var bytes = Encoding.UTF8.GetBytes(text);
        SendToPty(bytes);
    }

    async void CopySelectionToClipboard()
    {
        var sel = TextSelection.Normalize(selAnchorCol, selAnchorRow, selCurrentCol, selCurrentRow);
        string text;
        lock (bufferLock)
        {
            text = TextSelection.ExtractText(parser.ActiveBuffer, sel);
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null)
            await clipboard.SetTextAsync(text);

        hasSelection = false;
        MarkDirty();
    }

    TextSelection? GetNormalizedSelection()
    {
        if (!hasSelection)
            return null;
        return TextSelection.Normalize(selAnchorCol, selAnchorRow, selCurrentCol, selCurrentRow);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var overlays = host.GetProviders<IRenderOverlay>();

        // Snapshot the active buffer under lock so render doesn't block PTY reads
        ScreenBuffer snapshot;
        bool cursorVis;
        var snapStart = profiler.Enabled ? Stopwatch.GetTimestamp() : 0;
        lock (bufferLock)
        {
            snapshot = parser.ActiveBuffer.Snapshot();
            cursorVis = parser.CursorVisible;
        }
        if (profiler.Enabled)
        {
            profiler.RecordSnapshot(Stopwatch.GetTimestamp() - snapStart);
        }

        context.Custom(
            new TerminalDrawOperation(
                bounds,
                snapshot,
                renderer,
                GetNormalizedSelection(),
                overlays,
                cursorVisible: cursorVis,
                readOnly: isReadOnly
            )
        );
    }

    sealed class TerminalDrawOperation : ICustomDrawOperation
    {
        readonly Rect bounds;
        readonly ScreenBuffer snapshot;
        readonly TerminalRenderer renderer;
        readonly TextSelection? selection;
        readonly IReadOnlyList<IRenderOverlay> overlays;
        readonly bool cursorVisible;
        readonly bool readOnly;

        public TerminalDrawOperation(
            Rect bounds,
            ScreenBuffer snapshot,
            TerminalRenderer renderer,
            TextSelection? selection,
            IReadOnlyList<IRenderOverlay> overlays,
            bool cursorVisible = true,
            bool readOnly = false
        )
        {
            this.bounds = bounds;
            this.snapshot = snapshot;
            this.renderer = renderer;
            this.selection = selection;
            this.overlays = overlays;
            this.cursorVisible = cursorVisible;
            this.readOnly = readOnly;
        }

        public Rect Bounds => bounds;

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
            {
                return;
            }

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;

            renderer.Render(
                canvas,
                snapshot,
                (float)bounds.Width,
                selection,
                overlays,
                cursorVisible,
                readOnly
            );
        }
    }
}
