namespace Centaur.Core.Terminal;

/// <summary>
/// The numeric parameters of one CSI sequence, with the VT rule applied that a missing or
/// zero parameter means the command default.
/// </summary>
readonly struct CsiArgs
{
    readonly List<int> values;

    public CsiArgs(List<int> values)
    {
        this.values = values;
    }

    public int Get(int index, int defaultValue = 1) =>
        index < values.Count && values[index] > 0 ? values[index] : defaultValue;
}
