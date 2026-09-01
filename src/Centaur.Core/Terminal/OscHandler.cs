using System.Text;

namespace Centaur.Core.Terminal;

/// <summary>
/// Handles OSC (Operating System Command) sequences: the window title and icon name, the
/// working directory, the 256-color palette and the default fore/background, hyperlinks,
/// clipboard access, and semantic prompt marks. Holds the state those sequences set, which
/// callers read through <see cref="VtParser.Osc"/>.
/// </summary>
public sealed class OscHandler
{
    TerminalTheme theme;
    readonly SgrPen pen;
    readonly Action<string> reply;

    internal OscHandler(TerminalTheme theme, SgrPen pen, Action<string> reply)
    {
        this.theme = theme;
        this.pen = pen;
        this.reply = reply;

        DefaultForeground = theme.Foreground;
        DefaultBackground = theme.Background;
        for (int i = 0; i < Palette.Length; i++)
        {
            Palette[i] = theme.GetColor(i);
        }
    }

    /// <summary>
    /// Rebases the default colours and the 256-colour palette on a new theme. A theme change is
    /// a deliberate reset, so any OSC 4/10/11 overrides a program had set are dropped with it.
    /// </summary>
    internal void ApplyTheme(TerminalTheme next)
    {
        theme = next;
        DefaultForeground = next.Foreground;
        DefaultBackground = next.Background;
        for (var i = 0; i < Palette.Length; i++)
        {
            Palette[i] = next.GetColor(i);
        }
    }

    public string? WindowTitle { get; private set; } // OSC 0/2
    public string? IconName { get; private set; } // OSC 0/1
    public string? WorkingDirectory { get; private set; } // OSC 7
    public uint[] Palette { get; } = new uint[256]; // OSC 4/104
    public uint DefaultForeground { get; private set; } // OSC 10
    public uint DefaultBackground { get; private set; } // OSC 11
    public int? LastExitCode { get; private set; } // OSC 133;D;<code>

    /// <summary>Fired for OSC 52 clipboard writes/clears (read requests reply instead).</summary>
    public event Action<ClipboardRequest>? ClipboardChanged;

    /// <summary>Dispatches one complete OSC payload, everything between the introducer and
    /// its terminator. <paramref name="buffer"/> is the screen currently on display, which
    /// the semantic prompt marks attach to.</summary>
    internal void Dispatch(ReadOnlySpan<byte> payload, ScreenBuffer buffer)
    {
        if (payload.IsEmpty)
        {
            return;
        }

        var text = Encoding.UTF8.GetString(payload);
        var semi = text.IndexOf(';');
        var rest = semi >= 0 ? text[(semi + 1)..] : "";
        if (!int.TryParse(semi >= 0 ? text[..semi] : text, out var code))
        {
            return;
        }

        if (!TrySetLabel(code, rest))
        {
            Handle(code, rest, buffer);
        }
    }

    /// <summary>The codes that only record a string, split out to keep the main switch small.</summary>
    bool TrySetLabel(int code, string rest)
    {
        switch (code)
        {
            case 0: // set both the window title and the icon name
                WindowTitle = rest;
                IconName = rest;
                return true;
            case 1:
                IconName = rest;
                return true;
            case 2:
                WindowTitle = rest;
                return true;
            case 7: // report working directory
                WorkingDirectory = rest;
                return true;
            default:
                return false;
        }
    }

    void Handle(int code, string rest, ScreenBuffer buffer)
    {
        switch (code)
        {
            case 4:
                HandlePaletteColor(rest);
                break;
            case 8:
            {
                // "8;{params};{uri}" — an empty uri ends the current hyperlink.
                var semi = rest.IndexOf(';');
                var uri = semi >= 0 ? rest[(semi + 1)..] : "";
                pen.Hyperlink = uri.Length > 0 ? uri : null;
                break;
            }
            case 10:
                HandleDynamicColor(rest, ColorTarget.Foreground);
                break;
            case 11:
                HandleDynamicColor(rest, ColorTarget.Background);
                break;
            case 52:
                HandleClipboard(rest);
                break;
            case 104:
                HandleResetPalette(rest);
                break;
            case 133:
                HandleSemanticPrompt(rest, buffer);
                break;
        }
    }

    /// <summary>OSC 4: "{index};{spec-or-?}" sets or queries one palette entry.</summary>
    void HandlePaletteColor(string rest)
    {
        var semi = rest.IndexOf(';');
        if (semi < 0)
        {
            return;
        }
        if (!int.TryParse(rest[..semi], out var index) || index < 0 || index >= Palette.Length)
        {
            return;
        }

        var spec = rest[(semi + 1)..];
        if (spec == "?")
        {
            reply($"\x1b]4;{index};{XColor.Format(Palette[index])}\x07");
        }
        else if (XColor.TryParse(spec, out var color))
        {
            Palette[index] = color;
        }
    }

    /// <summary>OSC 104: bare resets the whole palette, ";n" resets one entry.</summary>
    void HandleResetPalette(string rest)
    {
        if (rest.Length == 0)
        {
            for (int i = 0; i < Palette.Length; i++)
            {
                Palette[i] = theme.GetColor(i);
            }
            return;
        }
        if (int.TryParse(rest, out var index) && index >= 0 && index < Palette.Length)
        {
            Palette[index] = theme.GetColor(index);
        }
    }

    /// <summary>OSC 10/11: sets or queries the default foreground or background.</summary>
    void HandleDynamicColor(string spec, ColorTarget target)
    {
        var foreground = target == ColorTarget.Foreground;
        if (spec == "?")
        {
            var current = foreground ? DefaultForeground : DefaultBackground;
            reply($"\x1b]{(foreground ? 10 : 11)};{XColor.Format(current)}\x07");
            return;
        }

        if (!XColor.TryParse(spec, out var color))
        {
            return;
        }
        if (foreground)
        {
            DefaultForeground = color;
        }
        else
        {
            DefaultBackground = color;
        }
    }

    /// <summary>OSC 52: "{selection};{base64-or-?}", where the selection defaults to c.</summary>
    void HandleClipboard(string rest)
    {
        var semi = rest.IndexOf(';');
        var selectionField = semi >= 0 ? rest[..semi] : "";
        var data = semi >= 0 ? rest[(semi + 1)..] : "";
        var selection = selectionField.Length > 0 ? selectionField[0] : 'c';

        if (data == "?")
        {
            // Read request: reply with empty contents (no clipboard wired yet).
            reply($"\x1b]52;{selection};\x07");
            return;
        }
        ClipboardChanged?.Invoke(new ClipboardRequest(selection, data));
    }

    /// <summary>OSC 133: "A", "B", "C", or "D[;exitcode]".</summary>
    void HandleSemanticPrompt(string rest, ScreenBuffer buffer)
    {
        switch (rest.Length > 0 ? rest[0] : '\0')
        {
            case 'A':
                buffer.Marks[buffer.cursorY] = PromptMark.Prompt;
                break;
            case 'B':
                buffer.Marks[buffer.cursorY] = PromptMark.Command;
                break;
            case 'C':
                buffer.Marks[buffer.cursorY] = PromptMark.Output;
                break;
            case 'D':
                var semi = rest.IndexOf(';');
                if (semi >= 0 && int.TryParse(rest[(semi + 1)..], out var exit))
                {
                    LastExitCode = exit;
                }
                break;
        }
    }
}
