using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// End-to-end tests for predictive local echo through the real render path:
/// <see cref="PredictionState"/> -> <see cref="PredictionOverlay"/> -> <see cref="TerminalRenderer"/>
/// -> bitmap. Each test renders the same buffer twice — once with no overlays (the control) and
/// once with the real <see cref="PredictionOverlay"/> — so any extra ink is provably the overlay's
/// predicted glyph, not the base grid. This is the riskiest part of the feature: that a predicted
/// glyph actually paints the same frame, before the shell has echoed anything into the buffer.
/// </summary>
public class PredictiveEchoE2ETests
{
    static readonly TerminalTheme theme = CatppuccinThemes.Macchiato;
    const int cols = 80;
    const int rows = 24;
    const int predictRow = 5;

    static ScreenBuffer NewBuffer() => new(cols, rows, theme);

    static IRenderOverlay[] With(PredictionOverlay overlay) => new IRenderOverlay[] { overlay };

    [Fact]
    public void PredictedGlyph_RendersBeforeEcho()
    {
        var state = new PredictionState();
        state.SetEnabled(true);
        state.PredictType('h', row: predictRow, startCol: 0, columns: cols);

        var overlay = new PredictionOverlay(state);
        var buffer = NewBuffer();
        var background = RenderProbe.ToColor(theme.Background);

        using var renderer = new TerminalRenderer(theme);
        using var control = RenderProbe.RenderToBitmap(buffer, renderer, cursorVisible: false);
        using var predicted = RenderProbe.RenderToBitmap(
            buffer,
            renderer,
            cursorVisible: false,
            overlays: With(overlay)
        );

        var baseInk = RenderProbe.ForegroundPixelCount(
            control,
            renderer,
            0,
            predictRow,
            background
        );
        var overlayInk = RenderProbe.ForegroundPixelCount(
            predicted,
            renderer,
            0,
            predictRow,
            background
        );

        Assert.Equal(0, baseInk);
        Assert.True(overlayInk > 0, "the predicted glyph drew no ink before the shell echoed it");
    }

    [Fact]
    public void ConfirmedPrediction_DrawsNothing()
    {
        var state = new PredictionState();
        state.SetEnabled(true);
        state.PredictType('h', row: predictRow, startCol: 0, columns: cols);

        // The shell echoes 'h': the real cursor advances to column 1, fully confirming our one
        // prediction. Reconcile clears it, so the overlay must not double-draw a stale glyph.
        state.Reconcile(realCol: 1, realRow: predictRow, altScreen: false, nowTicks: 0);
        Assert.False(state.HasPending);

        var overlay = new PredictionOverlay(state);
        var buffer = NewBuffer();
        var background = RenderProbe.ToColor(theme.Background);

        using var renderer = new TerminalRenderer(theme);
        using var predicted = RenderProbe.RenderToBitmap(
            buffer,
            renderer,
            cursorVisible: false,
            overlays: With(overlay)
        );

        var overlayInk = RenderProbe.ForegroundPixelCount(
            predicted,
            renderer,
            0,
            predictRow,
            background
        );
        Assert.Equal(0, overlayInk);
    }

    [Fact]
    public void DisabledState_DrawsNothing()
    {
        // Default-off: PredictType is ignored entirely, so the overlay has nothing to draw.
        var state = new PredictionState();
        state.PredictType('h', row: predictRow, startCol: 0, columns: cols);
        Assert.False(state.HasPending);

        var overlay = new PredictionOverlay(state);
        var buffer = NewBuffer();
        var background = RenderProbe.ToColor(theme.Background);

        using var renderer = new TerminalRenderer(theme);
        using var predicted = RenderProbe.RenderToBitmap(
            buffer,
            renderer,
            cursorVisible: false,
            overlays: With(overlay)
        );

        var overlayInk = RenderProbe.ForegroundPixelCount(
            predicted,
            renderer,
            0,
            predictRow,
            background
        );
        Assert.Equal(0, overlayInk);
    }

    [Fact]
    public void AltScreen_ClearsPredictionsBeforeRender()
    {
        var state = new PredictionState();
        state.SetEnabled(true);
        state.PredictType('h', row: predictRow, startCol: 0, columns: cols);
        Assert.True(state.HasPending);

        // A TUI switching to the alternate screen must wipe predictions — the grid is about to be
        // fully repainted and our anchor is meaningless.
        state.Reconcile(realCol: 0, realRow: predictRow, altScreen: true, nowTicks: 0);
        Assert.False(state.HasPending);

        var overlay = new PredictionOverlay(state);
        var buffer = NewBuffer();
        var background = RenderProbe.ToColor(theme.Background);

        using var renderer = new TerminalRenderer(theme);
        using var predicted = RenderProbe.RenderToBitmap(
            buffer,
            renderer,
            cursorVisible: false,
            overlays: With(overlay)
        );

        var overlayInk = RenderProbe.ForegroundPixelCount(
            predicted,
            renderer,
            0,
            predictRow,
            background
        );
        Assert.Equal(0, overlayInk);
    }
}
