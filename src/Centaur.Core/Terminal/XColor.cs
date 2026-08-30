using System.Globalization;

namespace Centaur.Core.Terminal;

/// <summary>
/// The X11 color-spec codec OSC 4/10/11 exchange colors in: "rgb:rr/gg/bb", with each channel
/// one to four hex digits wide.
/// </summary>
static class XColor
{
    /// <summary>Parses "rgb:rr/gg/bb" (or "rrrr/gggg/bbbb") into ARGB.</summary>
    public static bool TryParse(string spec, out uint color)
    {
        color = 0;
        if (!spec.StartsWith("rgb:", StringComparison.Ordinal))
        {
            return false;
        }
        var parts = spec[4..].Split('/');
        if (parts.Length != 3)
        {
            return false;
        }

        Span<byte> rgb = stackalloc byte[3];
        for (int i = 0; i < 3; i++)
        {
            if (!TryParseChannel(parts[i], out rgb[i]))
            {
                return false;
            }
        }
        color = 0xFF000000u | ((uint)rgb[0] << 16) | ((uint)rgb[1] << 8) | rgb[2];
        return true;
    }

    static bool TryParseChannel(string text, out byte value)
    {
        value = 0;
        if (
            text.Length is < 1 or > 4
            || !uint.TryParse(text, NumberStyles.HexNumber, null, out var raw)
        )
        {
            return false;
        }

        // X11 scales each channel so its width's max maps to 0xff: 1-digit "f" -> 0xff (not
        // 0x0f), 4-digit 0xffff -> 0xff, etc. Scale proportionally with rounding rather than
        // a bare right-shift.
        var max = (1u << (text.Length * 4)) - 1;
        value = (byte)(((raw * 255) + (max / 2)) / max);
        return true;
    }

    /// <summary>Formats ARGB as the reply form, with 16-bit channels.</summary>
    public static string Format(uint argb)
    {
        var r = (byte)(argb >> 16);
        var g = (byte)(argb >> 8);
        var b = (byte)argb;
        return $"rgb:{r:x2}{r:x2}/{g:x2}{g:x2}/{b:x2}{b:x2}";
    }
}
