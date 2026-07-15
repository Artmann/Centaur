using System.Diagnostics;
using System.Globalization;
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

    // The integer FPS only changes a handful of times per second, so caching the formatted
    // string skips the per-frame string.Format allocation. Used by tests to assert reuse.
    int cachedFps = -1;
    string cachedFpsText = "0 fps";

    /// <summary>
    /// Always on in the current build; exists for symmetry with the togglable
    /// <see cref="RenderProfiler.Enabled"/> so callers can ask "is anything self-updating
    /// here that needs a heartbeat?" without caring which overlay it is.
    /// </summary>
    public bool Enabled => true;

    internal string CachedFpsText => cachedFpsText;

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

        var fpsInt = (int)Math.Round(currentFps);
        if (fpsInt != cachedFps)
        {
            cachedFps = fpsInt;
            cachedFpsText = string.Format(CultureInfo.InvariantCulture, "{0} fps", fpsInt);
        }
        var text = cachedFpsText;

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
