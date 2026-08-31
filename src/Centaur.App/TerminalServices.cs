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

    /// <summary>The theme every pane renders with, falling back to the built-in one when no
    /// provider is registered (tests, or an extension that failed to activate). Resolved on
    /// first use, because the theme provider is registered while the host activates.</summary>
    public TerminalTheme Theme =>
        theme ??=
            Host.GetProvider<IThemeProvider>()
                ?.GetThemes()
                .FirstOrDefault(t => t.Id == "catppuccin-macchiato")
                ?.Theme
            ?? CatppuccinThemes.Macchiato;

    TerminalTheme? theme;
}
