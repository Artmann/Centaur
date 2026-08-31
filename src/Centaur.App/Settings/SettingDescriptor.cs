using Avalonia.Controls;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>The settings page's top-level sections, one per sidebar entry.</summary>
public enum SettingsTab
{
    General,
    Appearance,
}

/// <summary>
/// Everything an editor needs to render itself and write its value back. Rebuilt whenever the
/// page redraws, so an editor always reads the current settings rather than caching them.
/// </summary>
sealed class SettingsContext
{
    public required Settings Settings { get; init; }

    /// <summary>The themes the picker offers. Empty when no provider is registered.</summary>
    public required IReadOnlyList<ThemeInfo> Themes { get; init; }

    public required OverlayTheme Colors { get; init; }
}

/// <summary>
/// One setting, as the page and the search box see it. The page renders from these rather
/// than from hand-written layout methods, so adding an option is one property on
/// <see cref="Settings"/> plus one entry in <see cref="SettingsRegistry"/> - and
/// search, grouping and the tests pick it up with no further work.
/// </summary>
/// <param name="Id">The <see cref="SettingIds"/> id. Stable, and what a change is announced by.</param>
/// <param name="Tab">Which sidebar entry it appears under.</param>
/// <param name="Section">The heading it is grouped beneath within that tab.</param>
/// <param name="Keywords">Extra words search should find it by, for the terms a user is
/// likely to reach for that the title does not contain.</param>
/// <param name="CreateEditor">Builds the control that shows and writes the value.</param>
/// <param name="FullWidth">Renders the editor beneath its label rather than in a right-hand
/// column, for an editor too tall to sit beside one.</param>
sealed record SettingDescriptor(
    string Id,
    SettingsTab Tab,
    string Section,
    string Title,
    string Description,
    string[] Keywords,
    Func<SettingsContext, Control> CreateEditor,
    bool FullWidth = false
);
