using System.Diagnostics;
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
    readonly SKPaint fpsPaint;
    readonly SKPaint fpsBgPaint;
    readonly SKFont fpsFont;
    readonly TerminalTheme theme;

    int frameCount;
    readonly Stopwatch fpsStopwatch = Stopwatch.StartNew();
    double currentFps;

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

        fpsFont = new SKFont(typeface, fontSize * 0.85f);
        fpsPaint = new SKPaint
        {
            Color = new SKColor(theme.Foreground),
            IsAntialias = true
        };
        fpsBgPaint = new SKPaint
        {
            Color = new SKColor(theme.Background)
        };
    }

    public void Render(SKCanvas canvas, ScreenBuffer buffer, float canvasWidth, TextSelection? selection = null)
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

        DrawFps(canvas, canvasWidth);
    }

    void DrawFps(SKCanvas canvas, float canvasWidth)
    {
        frameCount++;
        var elapsed = fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsed >= 0.5)
        {
            currentFps = frameCount / elapsed;
            frameCount = 0;
            fpsStopwatch.Restart();
        }

        var text = $"{currentFps:F0} fps";
        var textWidth = fpsFont.MeasureText(text);
        var padding = 6f;
        var x = canvasWidth - textWidth - padding * 2;
        var y = padding;
        var height = fpsFont.Size * 1.4f;

        fpsBgPaint.Color = new SKColor(theme.Background);
        canvas.DrawRect(x, y, textWidth + padding * 2, height, fpsBgPaint);

        fpsPaint.Color = new SKColor(theme.Palette[8]); // Subtle: bright black / surface2
        canvas.DrawText(text, x + padding, y + fpsFont.Size, fpsFont, fpsPaint);
    }

    public void Dispose()
    {
        textPaint.Dispose();
        backgroundPaint.Dispose();
        fpsPaint.Dispose();
        fpsBgPaint.Dispose();
        fpsFont.Dispose();
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
