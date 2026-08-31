namespace Centaur.Core.Terminal;

/// <summary>
/// The parameters of one CSI sequence as they arrive: the digits, the two separators, the
/// private prefix and the intermediate byte. One instance is reused for every sequence -
/// <see cref="Begin"/> clears it - so a stream of them allocates nothing.
/// </summary>
sealed class CsiSequence
{
    // Parallel lists: IsColon[i] is true when the separator before Values[i] was a colon
    // (':'), marking it as a sub-parameter of the preceding param. SGR needs the difference
    // to tell ESC[4:3m (curly underline) from ESC[4;3m (underline + italic).
    readonly List<int> values = new();
    readonly List<bool> colons = new();

    bool pendingColon;
    int currentParam;

    /// <summary>Numeric parameters, in the order they were written.</summary>
    public List<int> Values => values;

    /// <summary>Which of <see cref="Values"/> a colon introduced.</summary>
    public List<bool> IsColon => colons;

    /// <summary>Private prefix ('?', '&gt;', '=', '&lt;'), or 0 when there was none.</summary>
    public char Prefix { get; private set; }

    /// <summary>Intermediate byte (e.g. '$' in DECRQM's CSI ? Ps $ p), or 0.</summary>
    public char Intermediate { get; private set; }

    /// <summary>The parameters read with the VT rule that a missing or zero parameter means
    /// the command's default.</summary>
    public CsiArgs Args => new(values);

    /// <summary>Drops the previous sequence and starts reading a new one.</summary>
    public void Begin()
    {
        values.Clear();
        colons.Clear();
        pendingColon = false;
        currentParam = 0;
        Prefix = '\0';
        Intermediate = '\0';
    }

    /// <summary>Consumes everything a CSI sequence can carry ahead of its final byte: the
    /// digits, the two separators, the private prefix and the intermediate byte.</summary>
    public bool TryAccumulate(byte b)
    {
        if (b >= '0' && b <= '9')
        {
            currentParam = currentParam * 10 + (b - '0');
            return true;
        }
        if (b == ';')
        {
            Push();
            pendingColon = false;
            return true;
        }
        if (b == ':')
        {
            // Colon sub-parameter: the next param belongs to this param's group.
            Push();
            pendingColon = true;
            return true;
        }
        if (b >= 0x3C && b <= 0x3F)
        {
            // Private parameter prefix: '<' '=' '>' '?'
            Prefix = (char)b;
            return true;
        }
        if (b >= 0x20 && b <= 0x2F)
        {
            // Intermediate byte (e.g. '$' in DECRQM's CSI ? Ps $ p).
            Intermediate = (char)b;
            return true;
        }
        return false;
    }

    /// <summary>Closes the parameter being read. The final byte of a sequence implies one.</summary>
    public void Push()
    {
        values.Add(currentParam);
        colons.Add(pendingColon);
        currentParam = 0;
    }
}
