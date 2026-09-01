using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
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
            "Command",
            "The program each new pane starts. Applies to panes opened from now on, not to the ones already running.",
            ["shell", "shell command", "pwsh", "powershell", "cmd", "bash", "program"],
            context =>
                SettingsControls.Text(
                    context.Colors,
                    context.Settings.ShellCommand,
                    value =>
                    {
                        context.Settings.ShellCommand = value;
                        context.Settings.Save(SettingIds.Shell);
                    }
                ),
            FullWidth: true
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
                new NumberEditor(
                    context.Colors,
                    context.Settings.ScrollbackLines,
                    new NumberRange(0, 200000, 5000, 0, " lines"),
                    value =>
                    {
                        context.Settings.ScrollbackLines = (int)value;
                        context.Settings.Save(SettingIds.Scrollback);
                    }
                ).View
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
                    [(BellMode.Off, "Off"), (BellMode.Sound, "Sound"), (BellMode.Flash, "Flash")],
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
            "Size",
            "The terminal's text size. The grid re-measures as it changes.",
            ["font", "font size", "typeface", "zoom", "larger", "smaller", "dpi", "point"],
            context =>
                new NumberEditor(
                    context.Colors,
                    context.Settings.FontSize,
                    new NumberRange(8, 48, 1, 0, " pt"),
                    value =>
                    {
                        context.Settings.FontSize = value;
                        context.Settings.Save(SettingIds.FontSize);
                    }
                ).View
        ),
        new(
            SettingIds.LineHeight,
            SettingsTab.Appearance,
            "Text",
            "Line height",
            "Row height as a multiple of the font size. Lower packs more lines onto the screen.",
            ["line height", "spacing", "leading", "density", "rows"],
            context =>
                new NumberEditor(
                    context.Colors,
                    context.Settings.LineHeight,
                    new NumberRange(1.0, 2.0, 0.05, 2, "×"),
                    value =>
                    {
                        context.Settings.LineHeight = value;
                        context.Settings.Save(SettingIds.LineHeight);
                    }
                ).View
        ),
        new(
            SettingIds.CursorStyle,
            SettingsTab.Appearance,
            "Cursor",
            "Style",
            "The shape the cursor is drawn as. A program can still override this for itself.",
            ["cursor", "cursor style", "block", "underline", "bar", "caret", "beam", "shape"],
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
            "Blink",
            "Blinks while the pane has focus.",
            ["cursor", "cursor blink", "blink", "flash", "caret", "pulse"],
            context =>
                SettingsControls.Toggle(
                    context.Colors,
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
            "Opacity",
            "How solid the window background is. 100% is fully opaque.",
            ["window", "window opacity", "transparency", "translucent", "see-through", "alpha"],
            context =>
                new NumberEditor(
                    context.Colors,
                    // Shown as a percentage rather than as the 0.5-to-1.0 fraction the setting
                    // stores. Nobody reads "0.85" as a share of anything.
                    context.Settings.WindowOpacity * 100,
                    new NumberRange(50, 100, 5, 0, "%"),
                    value =>
                    {
                        context.Settings.WindowOpacity = value / 100;
                        context.Settings.Save(SettingIds.WindowOpacity);
                    }
                ).View
        ),
        new(
            SettingIds.ContentPadding,
            SettingsTab.Appearance,
            "Window",
            "Padding",
            "The gap between the terminal and the window edge.",
            ["content padding", "window", "margin", "gap", "spacing", "inset", "border"],
            context =>
                new NumberEditor(
                    context.Colors,
                    context.Settings.ContentPadding,
                    new NumberRange(0, 64, 2, 0, " px"),
                    value =>
                    {
                        context.Settings.ContentPadding = (int)value;
                        context.Settings.Save(SettingIds.ContentPadding);
                    }
                ).View
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
            var empty = OverlayControls.CreateUiLabel("No themes are registered.", 12);
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
            },
            id => Swatch(context.Themes.First(theme => theme.Id == id).Theme)
        );
    }

    /// <summary>
    /// A theme shown in its own colours: the page background, ringed in the text colour, with the
    /// accent as a dot. Four proper nouns say nothing about which one is the light palette, and
    /// the segments are the one place on the page where the choice is a colour scheme.
    /// </summary>
    static Border Swatch(TerminalTheme theme) =>
        new Border
        {
            Width = 12,
            Height = 12,
            CornerRadius = new CornerRadius(6),
            Background = OverlayTheme.Brush(theme.Background),
            BorderBrush = OverlayTheme.Brush(theme.Foreground),
            BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Border
            {
                Width = 5,
                Height = 5,
                CornerRadius = new CornerRadius(3),
                Background = OverlayTheme.Brush(theme.Palette[4]),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            },
        };
}
