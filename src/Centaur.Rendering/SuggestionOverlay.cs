using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public class SuggestionOverlay : IRenderOverlay
{
    readonly SuggestionState state;
    SKPaint? ghostPaint;

    public int Priority => 500;

    public SuggestionOverlay(SuggestionState state)
    {
        this.state = state;
    }

    public void Render(
        SKCanvas canvas,
        float canvasWidth,
        TerminalTheme theme,
        SKFont baseFont,
        SKTypeface typeface
    )
    {
        var (text, col, row) = state.Read();
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ghostPaint ??= new SKPaint { IsAntialias = true };
        ghostPaint.Color = new SKColor(theme.Palette[8]).WithAlpha(128);

        var cellWidth = baseFont.MeasureText("M");
        var cellHeight = baseFont.Size * 1.2f;
        var textYOffset = cellHeight - (cellHeight - baseFont.Size) / 2;

        var x = col * cellWidth;
        var y = row * cellHeight + textYOffset;

        canvas.DrawText(text, x, y, baseFont, ghostPaint);
    }
}
