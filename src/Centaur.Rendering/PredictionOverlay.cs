using System.Diagnostics;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Draws Mosh-style predicted local echo: the glyphs the user just typed (before the shell has
/// echoed them) plus the predicted cursor position, read fresh from <see cref="PredictionState"/>
/// every frame. Priority 600 puts it above the base glyphs (so a prediction shows over an empty
/// cell) but below the FPS overlay (1000). Because it redraws purely from live state each frame,
/// a wrong prediction self-erases the moment <see cref="PredictionState"/> clears it.
/// </summary>
public class PredictionOverlay : IRenderOverlay
{
    readonly PredictionState state;
    SKPaint? glyphPaint;
    SKPaint? cursorPaint;

    public int Priority => 600;

    public PredictionOverlay(PredictionState state)
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
        // Sweep stale predictions on the render loop (~60×/s); a guess never lingers past ~120 ms.
        state.ExpireIfStale(Stopwatch.GetTimestamp(), staleMs: 120);

        var snapshot = state.Read();
        if (!snapshot.HasCursor)
        {
            return;
        }

        glyphPaint ??= new SKPaint { IsAntialias = true };
        cursorPaint ??= new SKPaint();

        // Derive cell metrics from the base font exactly as TerminalRenderer / SuggestionOverlay do.
        var cellWidth = baseFont.MeasureText("M");
        var cellHeight = baseFont.Size * 1.2f;
        var textYOffset = cellHeight - (cellHeight - baseFont.Size) / 2;

        // Predicted cursor block in the real cursor color, so a correct prediction is
        // indistinguishable from the real cursor. The real cursor is suppressed while pending.
        cursorPaint.Color = new SKColor(theme.Cursor);
        canvas.DrawRect(
            snapshot.CursorCol * cellWidth,
            snapshot.CursorRow * cellHeight,
            cellWidth,
            cellHeight,
            cursorPaint
        );

        // Predicted glyphs at full-opacity theme foreground — identical to a real echo, so the
        // glyph doesn't flicker or shift color when the shell confirms it.
        glyphPaint.Color = new SKColor(theme.Foreground);
        foreach (var cell in snapshot.Cells)
        {
            var x = cell.col * cellWidth;
            var y = cell.row * cellHeight + textYOffset;
            canvas.DrawText(cell.character.ToString(), x, y, baseFont, glyphPaint);
        }
    }
}
