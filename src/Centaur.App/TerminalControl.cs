using System.Buffers;
using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Centaur.App.Menus;
using Centaur.App.Splits;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

public class TerminalControl : Control, IPaneTerminal
{
    Control IPaneTerminal.View => this;

    bool IPaneTerminal.Focus() => Focus();

    readonly ExtensionHost host;
    readonly INotificationService notifications;
    readonly TerminalRenderer renderer;
    readonly RenderProfiler profiler;
    readonly PaneFrameLoop frames;

    // The screens, the scrollback view and the selection, behind the lock they share.
    readonly TerminalSurface surface;

    readonly ShellChannel shell;
    readonly InlineSuggestions suggestions;
    readonly TerminalInput input;
    readonly TerminalMouse mouse;
    readonly TerminalClipboard clipboard;
    readonly TerminalOverlays overlays;
    readonly KeyShortcutTable shortcuts;

    public string? WorkingDirectory => shell.WorkingDirectory;

    public event Action? WorkingDirectoryChanged;

    public TerminalControl(TerminalServices services, string? initialWorkingDirectory = null)
    {
        host = services.Host;
        notifications = services.Notifications;

        var theme = services.Theme;

        profiler = services.Profiler;
        renderer = new TerminalRenderer(theme, profiler: profiler);
        frames = CreateFrameLoop(services.FpsOverlay);

        surface = new TerminalSurface(theme, renderer, frames.MarkDirty);
        shell = CreateShell(services.Settings, initialWorkingDirectory);

        suggestions = new InlineSuggestions(
            services.Suggestions,
            surface.Parser,
            surface.BufferLock,
            frames.MarkDirty
        );
        input = new TerminalInput(shell, suggestions, host.Events, surface.Parser);
        mouse = new TerminalMouse(surface, shell);
        clipboard = new TerminalClipboard(this, surface, shell, notifications, frames.MarkDirty);
        overlays = new TerminalOverlays(this, services, theme, input.RunCommand);
        shortcuts = BuildShortcuts();

        Focusable = true;
        ClipToBounds = true;

        ContextMenu = BuildContextMenu();

        // A right-click a program received must not also open our menu. Shift+right-click
        // routes local, so the menu stays reachable.
        AddHandler(ContextRequestedEvent, (_, e) => e.Handled = mouse.SuppressContextMenu);
    }

    // The pty and everything hanging off it: output goes to the parser, a keystroke puts the
    // view back at the prompt, and the parser's protocol replies go straight back out.
    ShellChannel CreateShell(Settings settings, string? initialWorkingDirectory)
    {
        var channel = new ShellChannel(
            notifications,
            settings,
            initialWorkingDirectory,
            ParsePtyOutput,
            surface.ScrollToLiveEdge
        );
        channel.Exited += () => PtyExited?.Invoke();
        channel.WorkingDirectoryChanged += () => WorkingDirectoryChanged?.Invoke();
        surface.Parser.Reports.Respond += channel.Respond;
        return channel;
    }

    // The two things that need frames without the terminal itself changing: overlays on their
    // own clock, and the blink phase the loop advances for any cell carrying SGR 5/6.
    PaneFrameLoop CreateFrameLoop(FpsOverlayExtension fpsOverlay) =>
        new(this, () => fpsOverlay.Enabled || profiler.Enabled, () => renderer.HasBlinkingCells);

    public event Action<SplitDirection>? SplitRequested;
    public event Action? CloseRequested;

    // Ordered: the first entry that accepts the key wins, and anything not claimed here
    // falls through to the bytes the shell expects. Ctrl+Shift+P sits above the generic
    // Ctrl+letter path so it isn't swallowed as a control byte.
    KeyShortcutTable BuildShortcuts()
    {
        return new KeyShortcutTable()
            .Add(Key.PageUp, KeyModifiers.Shift, () => surface.ScrollByPage(up: true))
            .Add(Key.PageDown, KeyModifiers.Shift, () => surface.ScrollByPage(up: false))
            .Add(Key.Insert, KeyModifiers.Shift, clipboard.Paste)
            .Add(Key.Tab, KeyModifiers.None, input.AcceptSuggestion)
            .Add(Key.P, KeyModifiers.Control | KeyModifiers.Shift, ToggleProfiler)
            .Add(Key.C, KeyModifiers.Control, clipboard.CopyIfSelected)
            .Add(Key.V, KeyModifiers.Control, clipboard.Paste)
            .Add(Key.R, KeyModifiers.Control, overlays.OpenReverseSearch)
            .Add(Key.OemComma, KeyModifiers.Control, overlays.OpenSettings);
    }

    ContextMenu BuildContextMenu()
    {
        var context = new TerminalMenuContext
        {
            SelectionPresent = () => surface.Selection.HasSelection,
            ReadOnly = () => shell.IsReadOnly,
            ToggleReadOnlyRequested = () =>
            {
                shell.IsReadOnly = !shell.IsReadOnly;
                frames.MarkDirty();
            },
            CopyRequested = clipboard.Copy,
            PasteRequested = clipboard.Paste,
            PasteImageAsFileRequested = clipboard.PasteImageAsFile,
            SplitRequested = direction => this.SplitRequested?.Invoke(direction),
            CloseRequested = () => this.CloseRequested?.Invoke(),
        };

        return TerminalContextMenuBuilder.Create(host, context);
    }

    // The grid follows the control's size, and the pty is not started until there is one:
    // a shell launched at the placeholder 80x24 would lay out its prompt for the wrong width.
    protected override Size ArrangeOverride(Size finalSize)
    {
        var result = base.ArrangeOverride(finalSize);

        var (width, height) = (finalSize.Width, finalSize.Height);
        if (width <= 0 || height <= 0)
        {
            return result;
        }

        var newCols = Math.Max(1, (int)(width / renderer.cellWidth));
        var newRows = Math.Max(1, (int)(height / renderer.cellHeight));

        var changed = surface.ResizeTo(newCols, newRows);
        if (changed)
        {
            frames.MarkDirty();
        }

        if (frames.Running)
        {
            shell.Start(newCols, newRows);
        }

        if (changed)
        {
            shell.Resize(newCols, newRows);
        }

        return result;
    }

    public event Action? PtyExited;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        suggestions.Provider = host.GetProvider<ISuggestionProvider>();
        frames.Start();
        // PTY start is deferred until ArrangeOverride provides the real size
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        // Pause the animation loop. PTY and renderer survive detach so the control
        // can be re-parented (e.g. when a tab is split into panes) without losing state.
        // Final teardown happens in Close().
        frames.Stop();
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
        shell.Stop();
        renderer.Dispose();
    }

    // Called on the PTY read thread for every chunk the shell produced.
    void ParsePtyOutput(ReadOnlySequence<byte> bytes)
    {
        surface.Process(bytes, suggestions.NoteParsedOutput);

        // PTY bytes can change anything visible - buffer contents, cursor visibility
        // (DECTCEM), alt-screen swap, scrollback. One flag covers them all.
        frames.MarkDirty();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        var point = e.GetCurrentPoint(this);

        // A program with mouse tracking on owns the pointer, so it gets the click before the
        // selection does. Focus still moves here either way - the pane is being clicked.
        if (mouse.TryHandlePress(point.Properties, e.KeyModifiers, point.Position))
        {
            Focus();
            e.Handled = true;
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            // Right-click (and middle-click) bypass selection so the context menu can open
            // and so this control still receives focus for pane-focus tracking.
            Focus();
            return;
        }

        surface.BeginDrag(point.Position, e.ClickCount);
        frames.MarkDirty();
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);

        var point = e.GetCurrentPoint(this);
        if (mouse.TryHandleMove(point.Properties, e.KeyModifiers, point.Position))
        {
            e.Handled = true;
            return;
        }

        if (!surface.Selection.IsDragging)
        {
            return;
        }

        surface.ExtendDrag(point.Position);
        frames.MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);

        var position = e.GetPosition(this);
        if (mouse.TryHandleRelease(e.InitialPressMouseButton, e.KeyModifiers, position))
        {
            e.Handled = true;
            return;
        }

        if (!surface.Selection.IsDragging)
        {
            return;
        }

        e.Pointer.Capture(null);
        surface.EndDrag(position);
        frames.MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        var point = e.GetCurrentPoint(this);
        if (mouse.TryHandleWheel((int)e.Delta.Y, e.KeyModifiers, point.Position))
        {
            e.Handled = true;
            return;
        }

        if (surface.ScrollByWheel((int)e.Delta.Y))
        {
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (overlays.AnyOpen || !shell.IsConnected)
        {
            return;
        }

        if (shortcuts.TryHandle(e.Key, e.KeyModifiers))
        {
            e.Handled = true;
            return;
        }

        var bytes = input.Encode(e.Key, e.KeyModifiers);
        if (bytes != null)
        {
            shell.Send(bytes);
            e.Handled = true;
        }
    }

    void ToggleProfiler()
    {
        profiler.Enabled = !profiler.Enabled;
        // Profiler overlay visibility just toggled; also flips the heartbeat policy.
        frames.MarkDirty();
        notifications.Show(
            "Render Profiler",
            profiler.Enabled
                ? "Profiling ON — overlay + console dump every 2s. Ctrl+Shift+P to stop."
                : "Profiling OFF — final summary written to console.",
            NotificationSeverity.Info
        );
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (!shell.IsConnected || shell.IsReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        // Told before the send, so the read thread cannot mistake the echo of what was just
        // typed for the tail of a prompt.
        suggestions.NoteTypedText(e.Text);
        shell.Send(Encoding.UTF8.GetBytes(e.Text));
        e.Handled = true;
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        var overlays = host.GetProviders<IRenderOverlay>();

        var snapStart = profiler.Enabled ? Stopwatch.GetTimestamp() : 0;
        var snapshot = surface.Snapshot(out var cursorVis);
        if (profiler.Enabled)
        {
            profiler.RecordSnapshot(Stopwatch.GetTimestamp() - snapStart);
        }

        context.Custom(
            new TerminalDrawOperation(
                bounds,
                snapshot,
                renderer,
                surface.Selection.Normalized,
                overlays,
                cursorVisible: cursorVis,
                readOnly: shell.IsReadOnly,
                blinkVisible: frames.Blink.Visible
            )
        );
    }
}
