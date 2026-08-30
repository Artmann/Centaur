namespace Centaur.Core.Terminal;

public class ScrollbackBuffer
{
    readonly Cell[][] ring;
    int head;

    public int Count { get; private set; }
    public int Capacity { get; }

    /// <summary>How many rows above the live grid the view is currently parked, 0 meaning
    /// "at the live edge". Lives here rather than on the screen buffer because it is only
    /// ever meaningful relative to the history this type holds.</summary>
    public int Offset { get; private set; }

    /// <summary>True when this buffer keeps no history at all - the alternate screen and
    /// render snapshots both use one.</summary>
    public bool IsDisabled => Capacity == 0;

    public ScrollbackBuffer(int capacity)
    {
        Capacity = capacity;
        ring = new Cell[capacity][];
    }

    public void PushLine(ReadOnlySpan<Cell> row)
    {
        if (IsDisabled)
        {
            return;
        }

        if (ring[head] == null || ring[head].Length != row.Length)
        {
            ring[head] = new Cell[row.Length];
        }

        row.CopyTo(ring[head]);
        head = (head + 1) % Capacity;

        if (Count < Capacity)
        {
            Count++;
        }
    }

    public Cell[] GetLine(int index)
    {
        // index 0 = oldest, Count-1 = newest
        var ringIndex = (head - Count + index + Capacity) % Capacity;
        return ring[ringIndex];
    }

    public void ScrollUp(int lines)
    {
        Offset = Math.Min(Offset + lines, Count);
    }

    public void ScrollDown(int lines)
    {
        Offset = Math.Max(Offset - lines, 0);
    }

    public void ScrollToBottom()
    {
        Offset = 0;
    }

    public void Clear()
    {
        Count = 0;
        head = 0;
        Offset = 0;
    }
}
