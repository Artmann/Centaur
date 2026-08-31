using Avalonia.Controls;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// Every setting the page offers, in the order it renders them.
///
/// This is the file you edit to add an option: one entry here plus one property on
/// <see cref="Settings"/> and one id in <see cref="SettingIds"/>, and the tab,
/// the grouping, the search index and the tests pick it up with no further work.
/// </summary>
static class SettingsRegistry
{
    public static IReadOnlyList<SettingDescriptor> All => all;

    /// <summary>The settings on one tab, in registry order.</summary>
    public static IEnumerable<SettingDescriptor> ForTab(SettingsTab tab) =>
        all.Where(s => s.Tab == tab);

    static readonly SettingDescriptor[] all =
    [
        new(
            SettingIds.Shell,
            SettingsTab.General,
            "Shell",
            "Shell",
            "The program each new pane starts. Applies to panes opened from now on, not to the ones already running.",
            ["pwsh", "powershell", "cmd", "bash", "command", "program"],
            context =>
                SettingsControls.Text(
                    context.Colors,
                    context.Settings.ShellCommand,
                    value =>
                    {
                        context.Settings.ShellCommand = value;
                        context.Settings.Save(SettingIds.Shell);
                    }
                )
        ),
        new(
            SettingIds.StartDirectory,
            SettingsTab.General,
            "Shell",
            "Starting directory",
            "Where a new pane opens.",
            ["folder", "cwd", "working directory", "home", "path"],
            context => new StartDirectoryEditor(context).View,
            FullWidth: true
        ),
        new(
            SettingIds.Scrollback,
            SettingsTab.General,
            "Terminal",
            "Scrollback",
            "How many scrolled-off lines each pane keeps. 0 disables scrollback entirely.",
            ["history", "buffer", "lines", "scroll"],
            context =>
                SettingsControls.Number(
                    context.Colors,
                    context.Settings.ScrollbackLines,
                    new NumberRange(0, 200000, 1000, 0),
                    value =>
                    {
                        context.Settings.ScrollbackLines = (int)value;
                        context.Settings.Save(SettingIds.Scrollback);
                    }
                )
        ),
        new(
            SettingIds.Bell,
            SettingsTab.General,
            "Terminal",
            "Bell",
            "What happens when a program rings the terminal bell.",
            ["beep", "alert", "sound", "flash", "bel", "notification"],
            context =>
                SettingsControls.Choice(
                    context.Colors,
                    [(BellMode.Off, "Off"), (BellMode.Sound, "Sound"), (BellMode.Visual, "Flash")],
                    context.Settings.Bell,
                    value =>
                    {
                        context.Settings.Bell = value;
                        context.Settings.Save(SettingIds.Bell);
                    }
                )
        ),
        new(
            SettingIds.Theme,
            SettingsTab.Appearance,
            "Theme",
            "Theme",
            "The palette the terminal and the window chrome are painted from.",
            ["colours", "colors", "palette", "dark", "light", "scheme"],
            ThemePicker
        ),
        new(
            SettingIds.FontSize,
            SettingsTab.Appearance,
            "Text",
            "Font size",
            "The size of the terminal's text, in points. The grid re-measures as it changes.",
            ["typeface", "zoom", "larger", "smaller", "dpi", "point"],
            context =>
                SettingsControls.Number(
                    context.Colors,
                    context.Settings.FontSize,
                    new NumberRange(8, 48, 1, 0),
                    value =>
                    {
                        context.Settings.FontSize = value;
                        context.Settings.Save(SettingIds.FontSize);
                    }
                )
        ),
        new(
            SettingIds.LineHeight,
            SettingsTab.Appearance,
            "Text",
            "Line height",
            "Row height as a multiple of the font size. Lower packs more lines onto the screen.",
            ["spacing", "leading", "density", "rows"],
            context =>
                SettingsControls.Number(
                    context.Colors,
                    context.Settings.LineHeight,
                    new NumberRange(1.0, 2.0, 0.05, 2),
                    value =>
                    {
                        context.Settings.LineHeight = value;
                        context.Settings.Save(SettingIds.LineHeight);
                    }
                )
        ),
        new(
            SettingIds.CursorStyle,
            SettingsTab.Appearance,
            "Cursor",
            "Cursor style",
            "The shape the cursor is drawn as. A program can still override this for itself.",
            ["block", "underline", "bar", "caret", "beam", "shape"],
            context =>
                SettingsControls.Choice(
                    context.Colors,
                    [
                        (CursorStyle.Block, "Block"),
                        (CursorStyle.Underline, "Underline"),
                        (CursorStyle.Bar, "Bar"),
                    ],
                    context.Settings.CursorStyle,
                    value =>
                    {
                        context.Settings.CursorStyle = value;
                        context.Settings.Save(SettingIds.CursorStyle);
                    }
                )
        ),
        new(
            SettingIds.CursorBlink,
            SettingsTab.Appearance,
            "Cursor",
            "Cursor blink",
            "Whether the cursor blinks while the pane has focus.",
            ["blink", "flash", "caret", "pulse"],
            context =>
                SettingsControls.Choice(
                    context.Colors,
                    [(false, "Steady"), (true, "Blinking")],
                    context.Settings.CursorBlink,
                    value =>
                    {
                        context.Settings.CursorBlink = value;
                        context.Settings.Save(SettingIds.CursorBlink);
                    }
                )
        ),
        new(
            SettingIds.WindowOpacity,
            SettingsTab.Appearance,
            "Window",
            "Window opacity",
            "How solid the window background is. 1.00 is fully opaque.",
            ["transparency", "translucent", "see-through", "alpha", "blur"],
            context =>
                SettingsControls.Number(
                    context.Colors,
                    context.Settings.WindowOpacity,
                    new NumberRange(0.5, 1.0, 0.05, 2),
                    value =>
                    {
                        context.Settings.WindowOpacity = value;
                        context.Settings.Save(SettingIds.WindowOpacity);
                    }
                )
        ),
        new(
            SettingIds.ContentPadding,
            SettingsTab.Appearance,
            "Window",
            "Content padding",
            "The gap between the terminal and the window edge, in pixels.",
            ["margin", "gap", "spacing", "inset", "border"],
            context =>
                SettingsControls.Number(
                    context.Colors,
                    context.Settings.ContentPadding,
                    new NumberRange(0, 64, 2, 0),
                    value =>
                    {
                        context.Settings.ContentPadding = (int)value;
                        context.Settings.Save(SettingIds.ContentPadding);
                    }
                )
        ),
    ];

    /// <summary>
    /// The themes on offer, from whichever providers are registered. Falls back to a note rather
    /// than an empty row, because a picker with nothing in it looks broken.
    /// </summary>
    static Control ThemePicker(SettingsContext context)
    {
        if (context.Themes.Count == 0)
        {
            var empty = OverlayControls.CreateLabel("No themes are registered.", 12);
            empty.Foreground = context.Colors.Dim;
            return empty;
        }

        var options = context
            .Themes.Select(theme => (theme.Id, Label: theme.DisplayName))
            .ToArray();

        return SettingsControls.Choice(
            context.Colors,
            options,
            context.Settings.ThemeId,
            id =>
            {
                context.Settings.ThemeId = id;
                context.Settings.Save(SettingIds.Theme);
            }
        );
    }
}
