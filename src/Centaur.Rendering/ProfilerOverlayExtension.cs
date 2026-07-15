using System.Globalization;
using System.Text;
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
    static readonly CompositeFormat rowFormat = CompositeFormat.Parse("{0,-12}{1,7:F3}ms");
    static readonly CompositeFormat totalFormat = CompositeFormat.Parse(
        "total {0,6:F3}ms / {1:F1}ms budget"
    );
    static readonly CompositeFormat allocFormat = CompositeFormat.Parse("alloc {0:F1} KB/frame");
    static readonly CompositeFormat gen0Format = CompositeFormat.Parse("gen0 +{0}  ({1:F0} fps)");
    static readonly CompositeFormat sampleFormat = CompositeFormat.Parse("{0,-12}{1,7:F3}ms ");

    readonly RenderProfiler profiler;

    SKPaint? textPaint;
    SKPaint? bgPaint;
    SKPaint? barPaint;
    SKFont? font;

    // Cached label-column width — depends only on font.Size, so recompute alongside the lazy
    // font creation rather than once per frame.
    float labelColWidth;

    // Reused row scratch buffer to avoid the per-frame tuple-array allocation.
    readonly Row[] rows = new Row[7];

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
        if (font == null)
        {
            font = new SKFont(typeface, baseFont.Size * 0.85f);
            labelColWidth = font.MeasureText(
                string.Format(CultureInfo.InvariantCulture, sampleFormat, "", 0.0)
            );
        }

        var c = CultureInfo.InvariantCulture;
        var lineHeight = font.Size * 1.4f;
        var padding = 6f;

        // -1 in `pct` means "skip the bar for this row".
        rows[0] = new Row("snapshot", s.SnapshotMs, -1);
        rows[1] = new Row("clear", s.ClearMs, RenderProfiler.Percent(s.ClearMs, s.TotalMs));
        rows[2] = new Row(
            "background",
            s.BackgroundMs,
            RenderProfiler.Percent(s.BackgroundMs, s.TotalMs)
        );
        rows[3] = new Row(
            "glyphCollect",
            s.GlyphCollectMs,
            RenderProfiler.Percent(s.GlyphCollectMs, s.TotalMs)
        );
        rows[4] = new Row(
            "glyphDraw",
            s.GlyphDrawMs,
            RenderProfiler.Percent(s.GlyphDrawMs, s.TotalMs)
        );
        rows[5] = new Row("cursor", s.CursorMs, RenderProfiler.Percent(s.CursorMs, s.TotalMs));
        rows[6] = new Row("overlays", s.OverlayMs, -1);

        var x = padding;
        var barGap = 12f;
        var barMaxWidth = 110f;
        var barX = x + labelColWidth + barGap;

        var lineCount = rows.Length + 3; // rows + total + alloc + gen0
        var panelWidth = barX + barMaxWidth + padding;
        var panelHeight = lineHeight * lineCount + padding * 2;

        bgPaint.Color = new SKColor(theme.Background).WithAlpha(220);
        canvas.DrawRect(0, 0, panelWidth, panelHeight, bgPaint);

        var y = padding + font.Size;

        textPaint.Color = new SKColor(theme.Foreground);
        for (var i = 0; i < rows.Length; i++)
        {
            var row = rows[i];
            var text = string.Format(c, rowFormat, row.Label, row.Ms);
            canvas.DrawText(text, x, y, font, textPaint);

            if (row.Pct >= 0)
            {
                barPaint.Color = new SKColor(theme.Palette[4]);
                var w = (float)(barMaxWidth * Math.Clamp(row.Pct / 100.0, 0, 1));
                canvas.DrawRect(barX, y - font.Size * 0.8f, w, font.Size * 0.7f, barPaint);
            }

            y += lineHeight;
        }

        var overBudget = s.TotalMs > s.FrameBudgetMs;
        textPaint.Color = new SKColor(overBudget ? theme.Palette[1] : theme.Palette[2]);
        canvas.DrawText(
            string.Format(c, totalFormat, s.TotalMs, s.FrameBudgetMs),
            x,
            y,
            font,
            textPaint
        );
        y += lineHeight;

        textPaint.Color = new SKColor(theme.Foreground);
        canvas.DrawText(string.Format(c, allocFormat, s.AllocKbPerFrame), x, y, font, textPaint);
        y += lineHeight;

        canvas.DrawText(
            string.Format(c, gen0Format, s.Gen0PerWindow, s.Fps),
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

    readonly record struct Row(string Label, double Ms, double Pct);
}
