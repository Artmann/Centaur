using System.Reflection;
using System.Text;

namespace Centaur.Core.Terminal;

/// <summary>
/// Everything the terminal says about itself: the Device Attributes and Device Status replies,
/// the DECRQM mode report, and XTVERSION. Owns the response channel back to the pty, which is
/// also what the OSC handler answers colour and clipboard reads on.
/// </summary>
public sealed class DeviceReports
{
    /// <summary>Raw bytes to write to the pty's input. Every reply this type sends, and every
    /// OSC read the parser answers, arrives here.</summary>
    public event Action<byte[]>? Respond;

    // Version reported by XTVERSION. Resolved once from the assembly's build version
    // (set in Directory.Build.props) so it tracks releases instead of a hardcoded literal.
    public static string TerminalVersion { get; } = ResolveVersion();

    static string ResolveVersion()
    {
        var info = typeof(DeviceReports)
            .Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (!string.IsNullOrEmpty(info))
        {
            // Strip any "+<gitsha>" build metadata SourceLink may have appended.
            var plus = info.IndexOf('+', StringComparison.Ordinal);
            return plus >= 0 ? info[..plus] : info;
        }
        var version = typeof(DeviceReports).Assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
    }

    internal void Reply(string s) => Respond?.Invoke(Encoding.Latin1.GetBytes(s));

    /// <summary>DA1/DA2/DA3, told apart by the private prefix the sequence carried.</summary>
    internal void DeviceAttributes(char prefix)
    {
        switch (prefix)
        {
            case '>': // DA2 - secondary: device type 1, firmware 0, rom 0
                Reply("\x1b[>1;0;0c");
                break;
            case '=': // DA3 - tertiary: unit id, as DCS ! | <hex> ST
                Reply("\x1bP!|00000000\x1b\\");
                break;
            default: // DA1 - primary: VT220 (62) + ansi color (22)
                Reply("\x1b[?62;22c");
                break;
        }
    }

    /// <summary>DSR: either "I am fine" or the cursor position, which is why this one needs
    /// the screen.</summary>
    internal void DeviceStatus(int request, ScreenBuffer buffer)
    {
        switch (request)
        {
            case 5: // Report device status: terminal is functioning correctly.
                Reply("\x1b[0n");
                break;
            case 6: // CPR - report cursor position as 1-based row;col.
                Reply($"\x1b[{buffer.cursorY + 1};{buffer.cursorX + 1}R");
                break;
        }
    }

    /// <summary>DECRQM's answer: the mode that was asked about and how DecModes reported it.</summary>
    internal void ModeSetting(int mode, int setting) => Reply($"\x1b[?{mode};{setting}$y");

    /// <summary>XTVERSION (CSI &gt; q). Only the '&gt;' prefix with no intermediate asks it.</summary>
    internal void Version(char prefix, char intermediate)
    {
        if (prefix == '>' && intermediate == '\0')
        {
            Reply($"\x1bP>|Centaur({TerminalVersion})\x1b\\");
        }
    }
}
