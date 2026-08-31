namespace Centaur.Rendering;

/// <summary>
/// The SGR 5/6 blink clock: cells alternate between drawn and hidden every 500ms, the
/// conventional period. The phase is derived from the frame timestamp rather than counted, so
/// it stays correct across dropped frames and paused panes.
/// </summary>
public sealed class BlinkPhase
{
    const double halfPeriodMs = 500;

    /// <summary>Whether blinking cells are drawn in the current half-cycle.</summary>
    public bool Visible { get; private set; } = true;

    /// <summary>Advances to the phase for <paramref name="timestamp"/>, returning true when it
    /// flipped - i.e. when a terminal containing blinking cells needs a repaint.</summary>
    public bool Advance(TimeSpan timestamp)
    {
        var visible = (long)(timestamp.TotalMilliseconds / halfPeriodMs) % 2 == 0;
        if (visible == Visible)
        {
            return false;
        }

        Visible = visible;
        return true;
    }
}
