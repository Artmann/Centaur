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
using Centaur.Core.Hosting;
using Centaur.Core.Pty;
using Centaur.Core.Terminal;
using Centaur.Pty.Windows;
using Centaur.Rendering;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Centaur.App;

public class TerminalControl : Control
{
    readonly ExtensionHost host;
    readonly INotificationService notifications;
    readonly TerminalTheme theme;
    readonly ScreenBuffer initialBuffer;
    readonly TerminalRenderer renderer;
    readonly VtParser parser;
    readonly object bufferLock = new();

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

    public TerminalControl()
    {
        host = App.Services.GetRequiredService<ExtensionHost>();
        notifications = App.Services.GetRequiredService<INotificationService>();
        suggestionState = App.Services.GetRequiredService<SuggestionState>();

        var themeProvider = host.GetProvider<IThemeProvider>();
        theme =
            themeProvider?.GetThemes().FirstOrDefault(t => t.Id == "catppuccin-macchiato")?.Theme
            ?? CatppuccinThemes.Macchiato;

        renderer = new TerminalRenderer(theme);

        // Start with a default size; will resize once we know actual bounds
        initialBuffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(initialBuffer, theme);

        Focusable = true;
        ClipToBounds = true;
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
        ScheduleFrame();
        // PTY start is deferred until ArrangeOverride provides the real size
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        isAttached = false;
        StopPty();
        renderer.Dispose();
        readCts?.Dispose();
        base.OnDetachedFromVisualTree(e);
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

        InvalidateVisual();
        ScheduleFrame();
    }

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

            var options = new PtyOptions(executable: "powershell.exe", columns: cols, rows: rows);

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
        var (col, row) = PixelToGrid(e.GetPosition(this));
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
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

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
            if (e.Key == Key.Enter)
            {
                var input = ExtractUserInput();
                if (!string.IsNullOrWhiteSpace(input))
                {
                    host.Events.Publish(new CommandSubmittedEvent(input.Trim()));
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

            bytes = e.Key switch
            {
                Key.Enter => "\r"u8.ToArray(),
                Key.Back => new byte[] { 0x7F },
                Key.Tab => "\t"u8.ToArray(),
                Key.Escape => new byte[] { 0x1B },
                Key.Up => "\x1b[A"u8.ToArray(),
                Key.Down => "\x1b[B"u8.ToArray(),
                Key.Right => "\x1b[C"u8.ToArray(),
                Key.Left => "\x1b[D"u8.ToArray(),
                Key.Home => "\x1b[H"u8.ToArray(),
                Key.End => "\x1b[F"u8.ToArray(),
                Key.Insert => "\x1b[2~"u8.ToArray(),
                Key.Delete => "\x1b[3~"u8.ToArray(),
                Key.PageUp => "\x1b[5~"u8.ToArray(),
                Key.PageDown => "\x1b[6~"u8.ToArray(),
                Key.F1 => "\x1bOP"u8.ToArray(),
                Key.F2 => "\x1bOQ"u8.ToArray(),
                Key.F3 => "\x1bOR"u8.ToArray(),
                Key.F4 => "\x1bOS"u8.ToArray(),
                Key.F5 => "\x1b[15~"u8.ToArray(),
                Key.F6 => "\x1b[17~"u8.ToArray(),
                Key.F7 => "\x1b[18~"u8.ToArray(),
                Key.F8 => "\x1b[19~"u8.ToArray(),
                Key.F9 => "\x1b[20~"u8.ToArray(),
                Key.F10 => "\x1b[21~"u8.ToArray(),
                Key.F11 => "\x1b[23~"u8.ToArray(),
                Key.F12 => "\x1b[24~"u8.ToArray(),
                _ => null,
            };
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

        if (pty == null || string.IsNullOrEmpty(e.Text))
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
    }

    async void SendToPty(byte[] data)
    {
        if (pty == null)
        {
            return;
        }

        lock (bufferLock)
        {
            parser.ActiveBuffer.ScrollToBottom();
        }

        try
        {
            await pty.Input.WriteAsync(data);
            await pty.Input.FlushAsync();
        }
        catch (Exception ex)
        {
            notifications.Show("Input Error", ex.Message, NotificationSeverity.Error);
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
        lock (bufferLock)
        {
            snapshot = parser.ActiveBuffer.Snapshot();
            cursorVis = parser.CursorVisible;
        }

        context.Custom(
            new TerminalDrawOperation(
                bounds,
                snapshot,
                renderer,
                GetNormalizedSelection(),
                overlays,
                cursorVisible: cursorVis
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

        public TerminalDrawOperation(
            Rect bounds,
            ScreenBuffer snapshot,
            TerminalRenderer renderer,
            TextSelection? selection,
            IReadOnlyList<IRenderOverlay> overlays,
            bool cursorVisible = true
        )
        {
            this.bounds = bounds;
            this.snapshot = snapshot;
            this.renderer = renderer;
            this.selection = selection;
            this.overlays = overlays;
            this.cursorVisible = cursorVisible;
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
                cursorVisible
            );
        }
    }
}
