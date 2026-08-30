using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// Hands one frame to <see cref="TerminalRenderer"/> through Avalonia's Skia lease.
/// Immediate-mode: every frame is a full redraw of the snapshot it was given.
/// </summary>
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
