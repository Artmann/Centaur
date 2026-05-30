using System.Globalization;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Draws the live render-profiling panel (top-left, so it never collides with the top-right FPS
/// counter). Reads already-averaged values from the shared <see cref="RenderProfiler"/>; draws
/// nothing when profiling is off. Toggled via Ctrl+Shift+P (see TerminalControl.OnKeyDown).
/// </summary>
public class ProfilerOverlayExtension : IExtension, IRenderOverlay
{
    readonly RenderProfiler profiler;

    SKPaint? textPaint;
    SKPaint? bgPaint;
    SKPaint? barPaint;
    SKFont? font;

    public ProfilerOverlayExtension(RenderProfiler profiler)
    {
        this.profiler = profiler;
    }

    // Draw after the FPS overlay (1000); spatially separate anyway.
    public int Priority => 1001;

    public Task ActivateAsync(IExtensionContext context)
    {
        textPaint = new SKPaint { IsAntialias = true };
        bgPaint = new SKPaint();
        barPaint = new SKPaint();
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
        if (!profiler.Enabled || textPaint == null || bgPaint == null || barPaint == null)
        {
            return;
        }

        var s = profiler.GetDisplaySnapshot();
        font ??= new SKFont(typeface, baseFont.Size * 0.85f);

        var c = CultureInfo.InvariantCulture;
        var lineHeight = font.Size * 1.4f;
        var padding = 6f;

        // (label, ms, percent-of-total or -1 to skip the bar)
        var rows = new (string label, double ms, double pct)[]
        {
            ("snapshot", s.SnapshotMs, -1),
            ("clear", s.ClearMs, RenderProfiler.Percent(s.ClearMs, s.TotalMs)),
            ("background", s.BackgroundMs, RenderProfiler.Percent(s.BackgroundMs, s.TotalMs)),
            ("glyphCollect", s.GlyphCollectMs, RenderProfiler.Percent(s.GlyphCollectMs, s.TotalMs)),
            ("glyphDraw", s.GlyphDrawMs, RenderProfiler.Percent(s.GlyphDrawMs, s.TotalMs)),
            ("cursor", s.CursorMs, RenderProfiler.Percent(s.CursorMs, s.TotalMs)),
            ("overlays", s.OverlayMs, -1),
        };

        // The stage lines use a fixed-width format and a monospace font, so one sample line
        // measures the column the bars must start after (avoids overlapping the numbers).
        var x = padding;
        var barGap = 12f;
        var barMaxWidth = 110f;
        var labelColWidth = font.MeasureText(string.Format(c, "{0,-12}{1,7:F3}ms ", "", 0.0));
        var barX = x + labelColWidth + barGap;

        var lineCount = rows.Length + 3; // rows + total + alloc + gen0
        var panelWidth = barX + barMaxWidth + padding;
        var panelHeight = lineHeight * lineCount + padding * 2;

        bgPaint.Color = new SKColor(theme.Background).WithAlpha(220);
        canvas.DrawRect(0, 0, panelWidth, panelHeight, bgPaint);

        var y = padding + font.Size;

        textPaint.Color = new SKColor(theme.Foreground);
        foreach (var (label, ms, pct) in rows)
        {
            var text = string.Format(c, "{0,-12}{1,7:F3}ms", label, ms);
            canvas.DrawText(text, x, y, font, textPaint);

            if (pct >= 0)
            {
                barPaint.Color = new SKColor(theme.Palette[4]);
                var w = (float)(barMaxWidth * Math.Clamp(pct / 100.0, 0, 1));
                canvas.DrawRect(barX, y - font.Size * 0.8f, w, font.Size * 0.7f, barPaint);
            }

            y += lineHeight;
        }

        var overBudget = s.TotalMs > s.FrameBudgetMs;
        textPaint.Color = new SKColor(overBudget ? theme.Palette[1] : theme.Palette[2]);
        canvas.DrawText(
            string.Format(c, "total {0,6:F3}ms / {1:F1}ms budget", s.TotalMs, s.FrameBudgetMs),
            x,
            y,
            font,
            textPaint
        );
        y += lineHeight;

        textPaint.Color = new SKColor(theme.Foreground);
        canvas.DrawText(
            string.Format(c, "alloc {0:F1} KB/frame", s.AllocKbPerFrame),
            x,
            y,
            font,
            textPaint
        );
        y += lineHeight;

        canvas.DrawText(
            string.Format(c, "gen0 +{0}  ({1:F0} fps)", s.Gen0PerWindow, s.Fps),
            x,
            y,
            font,
            textPaint
        );
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        textPaint?.Dispose();
        bgPaint?.Dispose();
        barPaint?.Dispose();
        font?.Dispose();
        textPaint = null;
        bgPaint = null;
        barPaint = null;
        font = null;
        return ValueTask.CompletedTask;
    }
}
