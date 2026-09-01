namespace Centaur.Core.Terminal;

/// <summary>
/// The stable identity of each setting, used three ways: the settings page registers a
/// descriptor under one, <see cref="Settings.Changed"/> carries the one that just moved, and
/// the tests name settings by it.
///
/// They are strings rather than an enum because they cross an assembly boundary in both
/// directions - Core raises them, the app's page registers them - and because a setting removed
/// in a later release should leave a dead id behind rather than renumber its neighbours.
/// </summary>
public static class SettingIds
{
    public const string StartDirectory = "general.startDirectory";
    public const string LastFolder = "general.lastFolder";
    public const string Shell = "general.shell";
    public const string Scrollback = "general.scrollback";
    public const string Bell = "general.bell";

    public const string Theme = "appearance.theme";
    public const string FontSize = "appearance.fontSize";
    public const string LineHeight = "appearance.lineHeight";
    public const string CursorStyle = "appearance.cursorStyle";
    public const string CursorBlink = "appearance.cursorBlink";
    public const string WindowOpacity = "appearance.windowOpacity";
    public const string ContentPadding = "appearance.contentPadding";

    /// <summary>Settings a pane has to rebuild its renderer for, because they change the
    /// theme or the cell metrics the whole grid is laid out on.</summary>
    public static bool AffectsRendering(string id) =>
        id is Theme or FontSize or LineHeight or CursorStyle or CursorBlink or WindowOpacity or "";

    /// <summary>Settings the window chrome has to repaint or re-measure for.</summary>
    public static bool AffectsWindow(string id) =>
        id is Theme or ContentPadding or WindowOpacity or "";
}
