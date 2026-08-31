using System.Buffers;
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
    readonly TerminalRenderer renderer;
    readonly RenderProfiler profiler;
    readonly PaneFrameLoop frames;

    // The screens, the scrollback view and the selection, behind the lock they share.
    readonly TerminalSurface surface;

    readonly ShellChannel shell;
    readonly InlineSuggestions suggestions;
    readonly TerminalInput input;
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
        var fpsOverlay = services.FpsOverlay;
        frames = new PaneFrameLoop(this, () => fpsOverlay.Enabled || profiler.Enabled);

        surface = new TerminalSurface(theme, renderer);

        shell = new ShellChannel(
            notifications,
            services.Settings,
            initialWorkingDirectory,
            ParsePtyOutput,
            ScrollToLiveEdge
        );
        shell.Exited += () => PtyExited?.Invoke();
        shell.WorkingDirectoryChanged += () => WorkingDirectoryChanged?.Invoke();
        surface.Parser.Reports.Respond += shell.Respond;

        suggestions = new InlineSuggestions(
            services.Suggestions,
            surface.Parser,
            surface.BufferLock,
            frames.MarkDirty
        );
        input = new TerminalInput(shell, suggestions, host.Events);
        clipboard = new TerminalClipboard(this, surface, shell, frames.MarkDirty);
        overlays = new TerminalOverlays(this, services, theme, input.RunCommand);
        shortcuts = BuildShortcuts();

        Focusable = true;
        ClipToBounds = true;

        ContextMenu = BuildContextMenu();
    }

    public event Action<SplitDirection>? SplitRequested;
    public event Action? CloseRequested;

    // Ordered: the first entry that accepts the key wins, and anything not claimed here
    // falls through to the bytes the shell expects. Ctrl+Shift+P sits above the generic
    // Ctrl+letter path so it isn't swallowed as a control byte.
    KeyShortcutTable BuildShortcuts()
    {
        return new KeyShortcutTable()
            .Add(Key.PageUp, KeyModifiers.Shift, () => ScrollPage(up: true))
            .Add(Key.PageDown, KeyModifiers.Shift, () => ScrollPage(up: false))
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
            SplitRequested = direction => this.SplitRequested?.Invoke(direction),
            CloseRequested = () => this.CloseRequested?.Invoke(),
        };

        return TerminalContextMenuBuilder.Create(host, context);
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

    // Any keystroke the user sends puts them back at the prompt, so the view follows.
    void ScrollToLiveEdge()
    {
        surface.ScrollToLiveEdge();
        frames.MarkDirty();
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
        if (!surface.Selection.IsDragging)
        {
            return;
        }

        surface.ExtendDrag(e.GetPosition(this));
        frames.MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!surface.Selection.IsDragging)
        {
            return;
        }

        e.Pointer.Capture(null);
        surface.EndDrag(e.GetPosition(this));
        frames.MarkDirty();
        e.Handled = true;
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        base.OnPointerWheelChanged(e);

        if (!surface.ScrollByWheel((int)e.Delta.Y))
        {
            return;
        }

        frames.MarkDirty();
        e.Handled = true;
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

    // Scrollback paging, redrawing only when the surface actually moved.
    bool ScrollPage(bool up)
    {
        if (!surface.ScrollByPage(up))
        {
            return false;
        }

        frames.MarkDirty();
        return true;
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
                readOnly: shell.IsReadOnly
            )
        );
    }
}
