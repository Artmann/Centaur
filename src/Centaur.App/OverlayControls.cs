using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// Factory methods for the controls the overlays are built from, so both of them get
/// the same typeface, padding and focus behaviour without repeating the initialisers.
/// </summary>
static class OverlayControls
{
    /// <summary>Matches the terminal's own font stack so overlays read as part of it.</summary>
    public static readonly FontFamily MonoFont = new(
        "JetBrains Mono, Consolas, Courier New, monospace"
    );

    /// <summary>
    /// The shell's own UI stack, for surfaces that are part of the application rather than part
    /// of the terminal. The settings page is a page, not an overlay: rendering its labels in the
    /// terminal's monospace makes it read as terminal output, which is exactly what it is not.
    /// </summary>
    public static readonly FontFamily UiFont = new(
        "Segoe UI Variable Text, Segoe UI, Inter, system-ui, sans-serif"
    );

    /// <summary>
    /// A borderless single-line box. The focus adorner is cleared because the overlays
    /// draw their own focus affordance, and CornerRadius is flattened to match.
    /// </summary>
    public static TextBox CreateTextBox(Thickness borderThickness)
    {
        var box = new TextBox
        {
            BorderThickness = borderThickness,
            Padding = new Thickness(8, 8),
            MinHeight = 0,
            FontSize = 13,
            FontFamily = MonoFont,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            CornerRadius = new CornerRadius(0),
        };
        box.FocusAdorner = null;
        return box;
    }

    /// <summary>A label in the terminal's typeface, for the overlays that sit on top of it.</summary>
    public static TextBlock CreateLabel(
        string text,
        double fontSize,
        FontWeight weight = FontWeight.Normal
    ) => CreateLabel(text, fontSize, MonoFont, weight);

    /// <summary>A label in the shell's typeface, for the settings page.</summary>
    public static TextBlock CreateUiLabel(
        string text,
        double fontSize,
        FontWeight weight = FontWeight.Normal
    ) => CreateLabel(text, fontSize, UiFont, weight);

    static TextBlock CreateLabel(
        string text,
        double fontSize,
        FontFamily family,
        FontWeight weight
    ) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            FontFamily = family,
        };
}
