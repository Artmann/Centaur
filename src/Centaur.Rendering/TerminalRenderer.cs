using System.Reflection;
using SkiaSharp;
using Centaur.Core.Terminal;

namespace Centaur.Rendering;

public class TerminalRenderer : IDisposable
{
    internal readonly SKTypeface typeface;
    internal readonly SKFont font;
    readonly SKPaint textPaint;
    readonly SKPaint backgroundPaint;
    readonly TerminalTheme theme;

    public float cellWidth { get; }
    public float cellHeight { get; }

    public TerminalRenderer(TerminalTheme theme, float fontSize = 14f)
    {
        this.theme = theme;
        typeface = LoadEmbeddedFont() ?? SKTypeface.Default;
        font = new SKFont(typeface, fontSize);

        textPaint = new SKPaint
        {
            Color = new SKColor(theme.Foreground),
            IsAntialias = true
        };

        backgroundPaint = new SKPaint
        {
            Color = new SKColor(theme.Background)
        };

        cellWidth = font.MeasureText("M");
        cellHeight = fontSize * 1.2f;
    }

    public void Render(SKCanvas canvas, ScreenBuffer buffer, float canvasWidth,
                       TextSelection? selection = null, IReadOnlyList<IRenderOverlay>? overlays = null)
    {
        canvas.Clear(new SKColor(theme.Background));

        for (var y = 0; y < buffer.rows; y++)
        {
            for (var x = 0; x < buffer.columns; x++)
            {
                var cell = buffer[x, y];
                var px = x * cellWidth;
                var py = y * cellHeight;

                var selected = selection.HasValue && TextSelection.IsInSelection(x, y, selection.Value);
                var fg = selected ? cell.background : cell.foreground;
                var bg = selected ? cell.foreground : cell.background;

                // Draw background if not default black, or if selected
                if (bg != theme.Background || selected)
                {
                    backgroundPaint.Color = new SKColor(bg);
                    canvas.DrawRect(px, py, cellWidth, cellHeight, backgroundPaint);
                }

                // Draw character
                if (cell.character != ' ')
                {
                    textPaint.Color = new SKColor(fg);
                    canvas.DrawText(
                        cell.character.ToString(),
                        px,
                        py + cellHeight - (cellHeight - font.Size) / 2,
                        font,
                        textPaint
                    );
                }
            }
        }

        if (overlays != null)
        {
            foreach (var overlay in overlays)
            {
                overlay.Render(canvas, canvasWidth, theme, font, typeface);
            }
        }
    }

    public void Dispose()
    {
        textPaint.Dispose();
        backgroundPaint.Dispose();
        font.Dispose();
        typeface.Dispose();
    }

    static SKTypeface? LoadEmbeddedFont()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = "Centaur.Rendering.Fonts.JetBrainsMono-Regular.ttf";
        using var stream = assembly.GetManifestResourceStream(resourceName);
        return stream != null ? SKTypeface.FromStream(stream) : null;
    }
}
