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

    public static TextBlock CreateLabel(
        string text,
        double fontSize,
        FontWeight weight = FontWeight.Normal
    ) =>
        new()
        {
            Text = text,
            FontSize = fontSize,
            FontWeight = weight,
            FontFamily = MonoFont,
        };
}
