using System.Reflection;
using SkiaSharp;
using Centaur.Core.Terminal;

namespace Centaur.Rendering;

public class TerminalRenderer : IDisposable
{
    readonly SKTypeface typeface;
    readonly SKFont font;
    readonly SKPaint textPaint;
    readonly SKPaint backgroundPaint;

    public float cellWidth { get; }
    public float cellHeight { get; }

    public TerminalRenderer(float fontSize = 14f)
    {
        typeface = LoadEmbeddedFont() ?? SKTypeface.Default;
        font = new SKFont(typeface, fontSize);

        textPaint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true
        };

        backgroundPaint = new SKPaint
        {
            Color = SKColors.Black
        };

        cellWidth = font.MeasureText("M");
        cellHeight = fontSize * 1.2f;
    }

    public void Render(SKCanvas canvas, ScreenBuffer buffer, TextSelection? selection = null)
    {
        canvas.Clear(SKColors.Black);

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
                if (bg != 0xFF000000 || selected)
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
