using System.Diagnostics;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public class FpsOverlayExtension : IExtension, IRenderOverlay
{
    SKPaint? fpsPaint;
    SKPaint? fpsBgPaint;
    SKFont? fpsFont;

    int frameCount;
    readonly Stopwatch fpsStopwatch = Stopwatch.StartNew();
    double currentFps;

    public int Priority => 1000;

    public Task ActivateAsync(IExtensionContext context)
    {
        fpsPaint = new SKPaint { IsAntialias = true };
        fpsBgPaint = new SKPaint();
        return Task.CompletedTask;
    }

    public void Render(
        SKCanvas canvas,
        float canvasWidth,
        TerminalTheme theme,
        SKFont baseFont,
        SKTypeface typeface
    )
    {
        if (fpsPaint == null || fpsBgPaint == null)
        {
            return;
        }

        fpsFont ??= new SKFont(typeface, baseFont.Size * 0.85f);

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

        fpsPaint.Color = new SKColor(theme.Palette[8]);
        canvas.DrawText(text, x + padding, y + fpsFont.Size, fpsFont, fpsPaint);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        fpsPaint?.Dispose();
        fpsBgPaint?.Dispose();
        fpsFont?.Dispose();
        fpsPaint = null;
        fpsBgPaint = null;
        fpsFont = null;
        return ValueTask.CompletedTask;
    }
}
