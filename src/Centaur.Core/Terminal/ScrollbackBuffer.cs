namespace Centaur.Core.Terminal;

public class ScrollbackBuffer
{
    readonly Cell[][] ring;
    int head;

    public int Count { get; private set; }
    public int Capacity { get; }

    public ScrollbackBuffer(int capacity)
    {
        Capacity = capacity;
        ring = new Cell[capacity][];
    }

    public void PushLine(ReadOnlySpan<Cell> row)
    {
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

    public void Clear()
    {
        Count = 0;
        head = 0;
    }
}
