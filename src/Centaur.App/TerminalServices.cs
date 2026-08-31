using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;

namespace Centaur.App;

/// <summary>
/// Everything a terminal pane needs from the application, resolved once at startup and
/// handed down App → MainWindow → TabManager → TerminalControl.
///
/// The panes used to reach back up to App's static service provider for these, which made
/// the whole window graph mutually dependent and left a pane impossible to construct
/// without a running application. Passing them explicitly keeps the dependencies visible
/// at each construction site.
/// </summary>
public sealed class TerminalServices
{
    public required ExtensionHost Host { get; init; }
    public required INotificationService Notifications { get; init; }
    public required SuggestionState Suggestions { get; init; }
    public required CommandHistory CommandHistory { get; init; }
    public required ReverseSearchState ReverseSearch { get; init; }
    public required Settings Settings { get; init; }
    public required RenderProfiler Profiler { get; init; }
    public required FpsOverlayExtension FpsOverlay { get; init; }

    /// <summary>The theme every pane renders with, taken from <see cref="Settings.ThemeId"/>.
    /// Resolved on first use, because the theme provider is registered while the host
    /// activates.</summary>
    public TerminalTheme Theme => theme ??= Resolve();

    /// <summary>Every theme on offer, for the settings page's picker. Empty when no provider is
    /// registered, which is the case in tests.</summary>
    public IReadOnlyList<ThemeInfo> Themes => Host.GetProvider<IThemeProvider>()?.GetThemes() ?? [];

    /// <summary>
    /// Starts following <see cref="Settings.ThemeId"/>. Called once, at startup, so that this
    /// subscription runs before any window's or pane's - by the time they handle the same
    /// change, <see cref="Theme"/> already answers with the new theme.
    /// </summary>
    public void WatchSettings()
    {
        Settings.Changed += id =>
        {
            if (id is SettingIds.Theme or "")
            {
                theme = null;
            }
        };
    }

    /// <summary>
    /// Falls back to the built-in theme when the id names something no provider offers - a
    /// hand-edited settings file, or an extension that used to supply it and no longer does.
    /// The user is told, because otherwise the app silently ignores what their file says.
    /// </summary>
    TerminalTheme Resolve()
    {
        var themes = Themes;
        if (themes.Count == 0)
        {
            return CatppuccinThemes.Macchiato;
        }

        var match = themes.FirstOrDefault(t => t.Id == Settings.ThemeId);
        if (match != null)
        {
            return match.Theme;
        }

        Notifications.Show(
            "Unknown theme",
            $"No theme is registered as \"{Settings.ThemeId}\". Using the default instead; "
                + "pick one in Settings → Appearance → Theme.",
            NotificationSeverity.Warning
        );

        return themes.FirstOrDefault(t => t.Id == Settings.DefaultThemeId)?.Theme
            ?? themes[0].Theme;
    }

    TerminalTheme? theme;
}
