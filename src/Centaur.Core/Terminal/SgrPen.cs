using System.Collections.Frozen;

namespace Centaur.Core.Terminal;

/// <summary>SGR target for an extended-color attribute (38/48/58), and for OSC 10/11.</summary>
enum ColorTarget
{
    Foreground,
    Background,
    Underline,
}

/// <summary>
/// The SGR pen: the colours and text styles ESC[...m selects, held as the <see cref="Cell"/>
/// template every printed character is stamped from. Cells are immutable records, so the
/// template doubles as the value written to the grid.
/// </summary>
sealed class SgrPen
{
    // Style-only SGR codes, as edits to the pen template. The codes that need the theme
    // (colours) or a sub-parameter (4:N underline) are handled in ApplyGroup instead.
    static readonly FrozenDictionary<int, Func<Cell, Cell>> styleOps = new Dictionary<
        int,
        Func<Cell, Cell>
    >
    {
        [1] = c => c with { bold = true },
        [2] = c => c with { faint = true },
        [3] = c => c with { italic = true },
        [5] = c => c with { blink = true },
        [6] = c => c with { blink = true }, // Ghostty treats rapid blink (6) as blink (5).
        [7] = c => c with { inverse = true },
        [8] = c => c with { invisible = true },
        [9] = c => c with { strikethrough = true },
        [21] = c => c with { underline = UnderlineStyle.Double },
        [22] = c => c with { bold = false, faint = false }, // resets both
        [23] = c => c with { italic = false },
        [24] = c => c with { underline = UnderlineStyle.None },
        [25] = c => c with { blink = false },
        [27] = c => c with { inverse = false },
        [28] = c => c with { invisible = false },
        [29] = c => c with { strikethrough = false },
        [53] = c => c with { overline = true },
        [55] = c => c with { overline = false },
        // 0 is the sentinel meaning "inherit the foreground color".
        [59] = c => c with { underlineColor = 0 },
    }.ToFrozenDictionary();

    readonly TerminalTheme theme;
    Cell template;

    public SgrPen(TerminalTheme theme)
    {
        this.theme = theme;
        template = new Cell(' ', theme.Foreground, theme.Background);
    }

    /// <summary>Pen colours. DECSC/DECRC go through <see cref="Snapshot"/> instead, which
    /// carries the style flags with them.</summary>
    uint Foreground
    {
        set => template = template with { foreground = value };
    }

    uint Background
    {
        set => template = template with { background = value };
    }

    /// <summary>Active OSC 8 hyperlink target applied to printed cells (null when none).</summary>
    public string? Hyperlink
    {
        get => template.hyperlink;
        set => template = template with { hyperlink = value };
    }

    /// <summary>The cell to write for <paramref name="c"/> under the current styles.</summary>
    public Cell Paint(char c) => template with { character = c };

    /// <summary>The whole pen, for DECSC to hold until DECRC hands it back. The template is
    /// an immutable record, so the snapshot cannot be disturbed by later SGR.</summary>
    public Cell Snapshot() => template;

    /// <summary>DECRC: back to a snapshot, keeping the hyperlink the same way
    /// <see cref="Reset"/> does - OSC 8 opened it and only OSC 8 closes it.</summary>
    public void RestoreFrom(Cell saved) => template = saved with { hyperlink = template.hyperlink };

    /// <summary>SGR 0: back to the theme's colours with no styles. Keeps the hyperlink, which
    /// OSC 8 owns rather than SGR.</summary>
    public void Reset() =>
        template = new Cell(' ', theme.Foreground, theme.Background)
        {
            hyperlink = template.hyperlink,
        };

    /// <summary>Applies one ESC[...m sequence, given the raw CSI parameters and the colon
    /// flags that mark sub-parameters.</summary>
    public void Apply(List<int> values, List<bool> isColon)
    {
        var groups = GroupParams(values, isColon);
        for (var g = 0; g < groups.Count; g++)
        {
            g = ApplyGroup(groups, g);
        }
    }

    /// <summary>Groups params so colon sub-parameters attach to their primary param:
    /// ESC[4:3m -> one group [4,3]; ESC[38;2;1;2;3m -> groups [38],[2],[1],[2],[3].</summary>
    static List<List<int>> GroupParams(List<int> values, List<bool> isColon)
    {
        var groups = new List<List<int>>();
        for (var k = 0; k < values.Count; k++)
        {
            if (k == 0 || !isColon[k])
            {
                groups.Add(new List<int> { values[k] });
            }
            else
            {
                groups[^1].Add(values[k]);
            }
        }
        return groups;
    }

    /// <summary>Applies the group at <paramref name="g"/>, returning the last group it consumed
    /// (extended colours in the legacy semicolon form span several groups).</summary>
    int ApplyGroup(List<List<int>> groups, int g)
    {
        var group = groups[g];
        var p = group[0];

        if (p == 0)
        {
            Reset();
            return g;
        }

        if (p == 4)
        {
            // ESC[4m is single; ESC[4:Nm selects the style by sub-param.
            var style = group.Count > 1 ? MapUnderline(group[1]) : UnderlineStyle.Single;
            template = template with { underline = style };
            return g;
        }

        if (styleOps.TryGetValue(p, out var op))
        {
            template = op(template);
            return g;
        }

        if (ExtendedColorTarget(p) is { } target)
        {
            return ParseExtendedColor(groups, g, target);
        }

        ApplyIndexedColor(p);
        return g;
    }

    static ColorTarget? ExtendedColorTarget(int p) =>
        p switch
        {
            38 => ColorTarget.Foreground,
            48 => ColorTarget.Background,
            58 => ColorTarget.Underline,
            _ => null,
        };

    /// <summary>The base-16 colour codes, plus 39/49 for "back to the theme default".</summary>
    void ApplyIndexedColor(int p)
    {
        switch (p)
        {
            case >= 30 and <= 37:
                Foreground = theme.GetColor(p - 30);
                break;
            case 39:
                Foreground = theme.Foreground;
                break;
            case >= 40 and <= 47:
                Background = theme.GetColor(p - 40);
                break;
            case 49:
                Background = theme.Background;
                break;
            case >= 90 and <= 97:
                Foreground = theme.GetColor(p - 90 + 8);
                break;
            case >= 100 and <= 107:
                Background = theme.GetColor(p - 100 + 8);
                break;
        }
    }

    static UnderlineStyle MapUnderline(int code) =>
        code is >= 0 and <= 5 ? (UnderlineStyle)code : UnderlineStyle.Single;

    void SetColorTarget(ColorTarget target, uint color)
    {
        switch (target)
        {
            case ColorTarget.Foreground:
                Foreground = color;
                break;
            case ColorTarget.Background:
                Background = color;
                break;
            case ColorTarget.Underline:
                template = template with { underlineColor = color };
                break;
        }
    }

    static uint MakeRgb(int r, int g, int b) =>
        0xFF000000u | ((uint)(byte)r << 16) | ((uint)(byte)g << 8) | (byte)b;

    /// <summary>
    /// Parses an extended-color attribute (38/48/58) at group index <paramref name="g"/>,
    /// which comes in two forms: the colon form (ESC[38:2:r:g:b), carried entirely as
    /// sub-parameters of this group, and the legacy semicolon form (ESC[38;2;r;g;b), spread
    /// across the groups that follow. Returns the index of the last group consumed.
    /// </summary>
    int ParseExtendedColor(List<List<int>> groups, int g, ColorTarget target)
    {
        if (groups[g].Count > 1)
        {
            ApplyColonColor(groups[g], target);
            return g;
        }
        return ApplySemicolonColor(groups, g, target);
    }

    void ApplyColonColor(List<int> group, ColorTarget target)
    {
        var mode = group[1];
        if (mode == 5 && group.Count >= 3)
        {
            SetColorTarget(target, theme.GetColor(group[2]));
        }
        else if (mode == 2 && group.Count >= 5)
        {
            // The ITU form ESC[38:2::r:g:b carries a colorspace id at index 2, so the rgb
            // triple starts at 3 when 6+ components are present.
            var i = group.Count >= 6 ? 3 : 2;
            SetColorTarget(target, MakeRgb(group[i], group[i + 1], group[i + 2]));
        }
    }

    int ApplySemicolonColor(List<List<int>> groups, int g, ColorTarget target)
    {
        if (g + 1 >= groups.Count)
        {
            return g;
        }

        var mode = groups[g + 1][0];
        if (mode == 5 && g + 2 < groups.Count)
        {
            SetColorTarget(target, theme.GetColor(groups[g + 2][0]));
            return g + 2;
        }
        if (mode == 2 && g + 4 < groups.Count)
        {
            SetColorTarget(target, MakeRgb(groups[g + 2][0], groups[g + 3][0], groups[g + 4][0]));
            return g + 4;
        }
        return g + 1;
    }
}
