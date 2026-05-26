namespace Centaur.Core.Terminal;

/// <summary>
/// A tiny monotonic counter bumped once per non-empty parse pass on the PTY read thread.
/// The render thread watches <see cref="Version"/>: when it advances after a keystroke was
/// sent, the shell's echo has reached the screen, so the keystroke→pixel latency can be
/// measured. Lock-free — written on the read thread, read on the render thread.
/// </summary>
public class LatencyProbe
{
    long version;

    /// <summary>The current version. Increments each time fresh output is parsed.</summary>
    public long Version => Interlocked.Read(ref version);

    /// <summary>Record that a batch of shell output was just parsed into the buffer.</summary>
    public void Bump() => Interlocked.Increment(ref version);
}
