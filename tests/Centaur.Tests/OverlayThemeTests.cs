using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Centaur.App;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The colour rules the settings page is held to, checked against every registered theme rather
/// than against the one that happens to ship selected. A palette added later is covered here the
/// moment its provider returns it.
/// </summary>
public class OverlayThemeTests
{
    public static TheoryData<string> ThemeIds
    {
        get
        {
            var data = new TheoryData<string>();
            foreach (var theme in new CatppuccinThemeProvider().GetThemes())
            {
                data.Add(theme.Id);
            }

            return data;
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void Dim_text_clears_AA_against_the_card_it_sits_on(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));

        var ratio = OverlayTheme.Contrast(colors.Dim.Color, colors.Card.Color);

        Assert.True(
            ratio >= OverlayTheme.MinimumContrast,
            $"{themeId}: dim text is {ratio:F2}:1 on the card, below AA's "
                + $"{OverlayTheme.MinimumContrast}:1."
        );
    }

    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void Dim_text_stays_dimmer_than_the_foreground(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));

        var dim = OverlayTheme.Contrast(colors.Dim.Color, colors.Card.Color);
        var foreground = OverlayTheme.Contrast(colors.Foreground.Color, colors.Card.Color);

        Assert.True(
            dim < foreground,
            $"{themeId}: dim ({dim:F2}:1) is not dimmer than the foreground ({foreground:F2}:1)."
        );
    }

    /// <summary>
    /// The focus ring is the only thing that says which control the keyboard is on, so it is held
    /// to the 3:1 WCAG asks of a non-text indicator on every palette - Latte's accent is the
    /// honest test case, at 3.97:1.
    /// </summary>
    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void The_focus_ring_is_visible_against_the_card(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));

        var ratio = OverlayTheme.Contrast(colors.Accent.Color, colors.Card.Color);

        Assert.True(ratio >= 3.0, $"{themeId}: focus ring is {ratio:F2}:1 on the card.");
    }

    /// <summary>A chosen segment has to be seen as chosen without reading its label.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void The_chosen_chip_is_distinguishable_from_the_card(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));

        var ratio = OverlayTheme.Contrast(colors.Chip.Color, colors.Card.Color);

        Assert.True(
            ratio >= OverlayTheme.ChipContrast,
            $"{themeId}: chosen chip is only {ratio:F2}:1 on the card."
        );
    }

    /// <summary>The outline of a box or a stepper is what says "this is a control".</summary>
    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void A_control_boundary_is_visible_against_the_card(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));

        var ratio = OverlayTheme.Contrast(colors.Edge.Color, colors.Card.Color);

        Assert.True(
            ratio >= OverlayTheme.BoundaryContrast,
            $"{themeId}: a control outline is only {ratio:F2}:1 on the card."
        );
    }

    /// <summary>Hover and press have to be seen as a change without becoming a state.</summary>
    [AvaloniaTheory]
    [MemberData(nameof(ThemeIds))]
    public void Hover_and_press_sit_between_the_card_and_the_chosen_chip(string themeId)
    {
        var colors = new OverlayTheme(Theme(themeId));
        double Off(SolidColorBrush b) => OverlayTheme.Contrast(b.Color, colors.Card.Color);

        Assert.True(Off(colors.Hover) > 1.0, $"{themeId}: hover is invisible.");
        Assert.True(Off(colors.Press) > Off(colors.Hover), $"{themeId}: press is not past hover.");
        Assert.True(Off(colors.Chip) > Off(colors.Press), $"{themeId}: chip is not past press.");
    }

    static TerminalTheme Theme(string id) =>
        new CatppuccinThemeProvider().GetThemes().Single(t => t.Id == id).Theme;
}
