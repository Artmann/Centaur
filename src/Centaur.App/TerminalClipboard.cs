using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>What a paste turns into once the clipboard has been looked at.</summary>
public enum PasteRoute
{
    /// <summary>The clipboard holds nothing this terminal can paste.</summary>
    Nothing,

    /// <summary>Type the clipboard text, encoded for the terminal.</summary>
    Text,

    /// <summary>Hand the running program the Ctrl+V byte and let it read the clipboard
    /// itself, which is how a full-screen TUI makes its own attachment out of a picture.</summary>
    ClipboardKey,

    /// <summary>Write the picture out and type its path, which is all a shell can use.</summary>
    ImageFile,
}

/// <summary>
/// Copy and paste for one pane: the selection out to the system clipboard, and clipboard text
/// or images in as if they had been typed.
///
/// Split out of <see cref="TerminalControl"/>, which keeps only the gestures that reach here.
/// The clipboard is owned by the window rather than the control, so it is looked up per call -
/// a pane that has been re-parented, or is not in the visual tree at all, simply does nothing.
/// </summary>
public sealed class TerminalClipboard
{
    readonly Visual owner;
    readonly TerminalSurface surface;
    readonly ShellChannel shell;
    readonly INotificationService notifications;
    readonly Action markDirty;

    public TerminalClipboard(
        Visual owner,
        TerminalSurface surface,
        ShellChannel shell,
        INotificationService notifications,
        Action markDirty
    )
    {
        this.owner = owner;
        this.surface = surface;
        this.shell = shell;
        this.notifications = notifications;
        this.markDirty = markDirty;
    }

    /// <summary>Where pasted images are dropped so the shell has a path to name.</summary>
    static string ImageDirectory => Path.Combine(Path.GetTempPath(), "Centaur");

    /// <summary>Ctrl+C copies only when there is a selection; with none it declines, so the
    /// key goes to the shell as the interrupt the user meant.</summary>
    public bool CopyIfSelected()
    {
        if (!surface.Selection.HasSelection)
        {
            return false;
        }

        Copy();
        return true;
    }

    public async void Copy()
    {
        var text = surface.SelectedText();

        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard != null)
        {
            await clipboard.SetTextAsync(text);
        }

        surface.Selection.Clear();
        markDirty();
    }

    /// <summary>
    /// Text wins when the clipboard offers both. An image goes one of two ways: on the
    /// alternate screen a full-screen program is running that can read the clipboard itself,
    /// so it gets the Ctrl+V byte and decides - that is how Claude Code produces its inline
    /// attachment. Anywhere else a path is the only thing a shell can use, so the picture is
    /// written out and its name typed instead.
    /// </summary>
    public async void Paste()
    {
        await PasteFrom(Ready(), asFile: false);
    }

    /// <summary>The path route, unconditionally. The alternate-screen guess above is a guess,
    /// and this is the way back when the program on the other end ignored Ctrl+V.</summary>
    public async void PasteImageAsFile()
    {
        await PasteFrom(Ready(), asFile: true);
    }

    /// <summary>
    /// Which of the three things a paste turns into, given what the clipboard is holding and
    /// what is running on the other end. Text wins whenever it is there, because a clipboard
    /// carrying both is nearly always text with a rendering of it attached.
    /// </summary>
    public static PasteRoute Route(bool hasText, bool hasImage, bool alternateScreen, bool asFile)
    {
        if (hasText && !asFile)
        {
            return PasteRoute.Text;
        }

        if (!hasImage)
        {
            return PasteRoute.Nothing;
        }

        return alternateScreen && !asFile ? PasteRoute.ClipboardKey : PasteRoute.ImageFile;
    }

    async Task PasteFrom(IClipboard? clipboard, bool asFile)
    {
        if (clipboard == null)
        {
            return;
        }

        try
        {
            var text = asFile ? null : await clipboard.GetTextAsync();
            var hasText = !string.IsNullOrEmpty(text);
            var image = hasText ? null : await ClipboardImage.ReadAsync(clipboard);

            switch (Route(hasText, image != null, surface.Parser.IsAlternateScreen, asFile))
            {
                case PasteRoute.Text:
                    SendText(text!);
                    break;
                case PasteRoute.ClipboardKey:
                    shell.Send([0x16]);
                    break;
                case PasteRoute.ImageFile:
                    SendImagePath(image!);
                    break;
                default:
                    // Nothing pasteable on the clipboard is not worth interrupting for.
                    break;
            }
        }
        catch (Exception ex)
        {
            notifications.Show(
                "Paste Failed",
                $"The clipboard could not be read: {ex.Message}",
                NotificationSeverity.Error
            );
        }
    }

    /// <summary>The pane's clipboard, once it is clear a paste is allowed to happen at all.
    /// Read-only panes say so rather than dropping the keystroke, which is what typing into
    /// one does and is baffling for a deliberate Ctrl+V.</summary>
    IClipboard? Ready()
    {
        var clipboard = TopLevel.GetTopLevel(owner)?.Clipboard;
        if (clipboard == null)
        {
            return null;
        }

        if (shell.IsReadOnly)
        {
            notifications.Show(
                "Pane Is Read-Only",
                "Paste is disabled. Right-click the pane and clear Read-Only to enable it.",
                NotificationSeverity.Warning
            );
            return null;
        }

        return clipboard;
    }

    void SendImagePath(byte[] image)
    {
        string? path;
        try
        {
            path = ClipboardImage.Save(image, ImageDirectory);
        }
        catch (Exception ex)
        {
            notifications.Show(
                "Paste Failed",
                $"Could not write the image to \"{ImageDirectory}\": {ex.Message}",
                NotificationSeverity.Error
            );
            return;
        }

        if (path == null)
        {
            notifications.Show(
                "Paste Failed",
                "That clipboard image could not be read. Try copying it again.",
                NotificationSeverity.Error
            );
            return;
        }

        SendText(ClipboardImage.Quote(path));
    }

    /// <summary>
    /// Everything typed on the pane's behalf goes through <see cref="PasteEncoder"/>, which
    /// normalizes line endings and, when the program has asked for bracketed paste (DEC 2004),
    /// brackets the payload so a multi-line paste arrives as one block instead of submitting
    /// on its first newline.
    /// </summary>
    void SendText(string text)
    {
        var bracketed = surface.Parser.Modes.BracketedPasteMode;
        shell.Send(Encoding.UTF8.GetBytes(PasteEncoder.Encode(text, bracketed)));
    }
}
