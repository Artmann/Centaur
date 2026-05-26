using System.Collections.Concurrent;
using System.Diagnostics;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public class FpsOverlayExtension : IExtension, IRenderOverlay
{
    readonly LatencyProbe latencyProbe;
    readonly ConcurrentQueue<long> pendingKeystrokes = new();
    IDisposable? keystrokeSub;
    long lastSeenVersion;
    double lastLatencyMs;

    SKPaint? fpsPaint;
    SKPaint? fpsBgPaint;
    SKFont? fpsFont;

    int frameCount;
    readonly Stopwatch fpsStopwatch = Stopwatch.StartNew();
    double currentFps;

    public int Priority => 1000;

    /// <summary>The most recent keystroke→pixel latency in ms. Exposed for tests.</summary>
    public double LastLatencyMs => lastLatencyMs;

    public FpsOverlayExtension(LatencyProbe latencyProbe)
    {
        this.latencyProbe = latencyProbe;
    }

    public Task ActivateAsync(IExtensionContext context)
    {
        fpsPaint = new SKPaint { IsAntialias = true };
        fpsBgPaint = new SKPaint();

        lastSeenVersion = latencyProbe.Version;
        keystrokeSub = context.Events.Subscribe<KeystrokeSentEvent>(e =>
            pendingKeystrokes.Enqueue(e.TimestampTicks)
        );
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

        UpdateLatency();

        var text = $"{currentFps:F0} fps  {lastLatencyMs:F1} ms";
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

    // When the parse version advances, fresh shell output reached the buffer this frame — so
    // any keystroke sent before now has been echoed. Measure the round-trip and EMA-smooth it.
    void UpdateLatency()
    {
        var version = latencyProbe.Version;
        if (version == lastSeenVersion)
        {
            return;
        }
        lastSeenVersion = version;

        while (pendingKeystrokes.TryDequeue(out var ts))
        {
            var ms = Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
            lastLatencyMs = lastLatencyMs <= 0 ? ms : lastLatencyMs * 0.7 + ms * 0.3;
        }
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        keystrokeSub?.Dispose();
        fpsPaint?.Dispose();
        fpsBgPaint?.Dispose();
        fpsFont?.Dispose();
        fpsPaint = null;
        fpsBgPaint = null;
        fpsFont = null;
        return ValueTask.CompletedTask;
    }
}
