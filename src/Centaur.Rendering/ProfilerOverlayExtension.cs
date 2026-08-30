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

    // Panel geometry, in device-independent pixels.
    const float padding = 6f;
    const float barGap = 12f;
    const float barMaxWidth = 110f;

    readonly RenderProfiler profiler;

    SKPaint? textPaint;
    SKPaint? bgPaint;
    SKPaint? barPaint;
    SKFont? font;

    // Cached label-column width — depends only on font.Size, so recompute alongside the lazy
    // font creation rather than once per frame.
    float labelColWidth;

    // Reused scratch buffer to avoid the per-frame array allocation.
    readonly ProfilerStage[] stages = new ProfilerStage[ProfilerStages.Count];

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
        EnsureFont(baseFont, typeface);
        ProfilerStages.Fill(stages, s);

        var lineHeight = font!.Size * 1.4f;
        var barX = padding + labelColWidth + barGap;

        bgPaint.Color = new SKColor(theme.Background).WithAlpha(220);
        canvas.DrawRect(
            0,
            0,
            barX + barMaxWidth + padding,
            // stages + total + alloc + gen0
            lineHeight * (stages.Length + 3)
                + padding * 2,
            bgPaint
        );

        var y = padding + font.Size;
        y = DrawStages(canvas, theme, barX, lineHeight, y);
        DrawSummary(canvas, theme, s, lineHeight, y);
    }

    /// <summary>Lazily builds the panel font and the label-column width it implies.</summary>
    void EnsureFont(SKFont baseFont, SKTypeface typeface)
    {
        if (font != null)
        {
            return;
        }

        font = new SKFont(typeface, baseFont.Size * 0.85f);
        labelColWidth = font.MeasureText(
            string.Format(CultureInfo.InvariantCulture, sampleFormat, "", 0.0)
        );
    }

    /// <summary>Draws one row per stage, with a share bar for the stages that have one.
    /// Returns the baseline for the first summary row.</summary>
    float DrawStages(SKCanvas canvas, TerminalTheme theme, float barX, float lineHeight, float y)
    {
        textPaint!.Color = new SKColor(theme.Foreground);
        foreach (var stage in stages)
        {
            var text = string.Format(
                CultureInfo.InvariantCulture,
                rowFormat,
                stage.Label,
                stage.Ms
            );
            canvas.DrawText(text, padding, y, font, textPaint);

            if (stage.Share is { } share)
            {
                barPaint!.Color = new SKColor(theme.Palette[4]);
                var w = (float)(barMaxWidth * Math.Clamp(share / 100.0, 0, 1));
                canvas.DrawRect(barX, y - font!.Size * 0.8f, w, font.Size * 0.7f, barPaint);
            }

            y += lineHeight;
        }

        return y;
    }

    /// <summary>Draws the frame total (red once over budget) and the allocation rows.</summary>
    void DrawSummary(
        SKCanvas canvas,
        TerminalTheme theme,
        ProfilerSnapshot s,
        float lineHeight,
        float y
    )
    {
        var c = CultureInfo.InvariantCulture;
        var overBudget = s.TotalMs > s.FrameBudgetMs;

        textPaint!.Color = new SKColor(overBudget ? theme.Palette[1] : theme.Palette[2]);
        canvas.DrawText(
            string.Format(c, totalFormat, s.TotalMs, s.FrameBudgetMs),
            padding,
            y,
            font,
            textPaint
        );
        y += lineHeight;

        textPaint.Color = new SKColor(theme.Foreground);
        canvas.DrawText(
            string.Format(c, allocFormat, s.AllocKbPerFrame),
            padding,
            y,
            font,
            textPaint
        );
        y += lineHeight;

        canvas.DrawText(
            string.Format(c, gen0Format, s.Gen0PerWindow, s.Fps),
            padding,
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
