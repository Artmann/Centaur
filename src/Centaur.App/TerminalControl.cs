using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;

namespace Centaur.App;

public class TerminalControl : Control
{
    readonly ScreenBuffer buffer;
    readonly TerminalRenderer renderer;

    public TerminalControl()
    {
        buffer = new ScreenBuffer(80, 24);
        renderer = new TerminalRenderer();

        Focusable = true;
        ClipToBounds = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Key == Key.Enter)
        {
            buffer.cursorX = 0;
            buffer.cursorY++;
            if (buffer.cursorY >= buffer.rows)
            {
                buffer.cursorY = buffer.rows - 1;
                ScrollUp();
            }
        }
        else if (e.Key == Key.Back)
        {
            if (buffer.cursorX > 0)
            {
                buffer.cursorX--;
                buffer[buffer.cursorX, buffer.cursorY] = new Cell(' ');
            }
        }
        else
        {
            return; // Let OnTextInput handle regular characters
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnTextInput(TextInputEventArgs e)
    {
        base.OnTextInput(e);

        if (string.IsNullOrEmpty(e.Text))
            return;

        foreach (var c in e.Text)
        {
            buffer.Write(c);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    void ScrollUp()
    {
        for (var y = 0; y < buffer.rows - 1; y++)
        {
            for (var x = 0; x < buffer.columns; x++)
            {
                buffer[x, y] = buffer[x, y + 1];
            }
        }
        for (var x = 0; x < buffer.columns; x++)
        {
            buffer[x, buffer.rows - 1] = new Cell(' ');
        }
    }

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.Custom(new TerminalDrawOperation(bounds, buffer, renderer));
    }

    class TerminalDrawOperation : ICustomDrawOperation
    {
        readonly Rect bounds;
        readonly ScreenBuffer buffer;
        readonly TerminalRenderer renderer;

        public TerminalDrawOperation(Rect bounds, ScreenBuffer buffer, TerminalRenderer renderer)
        {
            this.bounds = bounds;
            this.buffer = buffer;
            this.renderer = renderer;
        }

        public Rect Bounds => bounds;

        public void Dispose() { }

        public bool Equals(ICustomDrawOperation? other) => false;

        public bool HitTest(Point p) => bounds.Contains(p);

        public void Render(ImmediateDrawingContext context)
        {
            var leaseFeature = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
            if (leaseFeature == null)
                return;

            using var lease = leaseFeature.Lease();
            var canvas = lease.SkCanvas;
            renderer.Render(canvas, buffer);
        }
    }
}
