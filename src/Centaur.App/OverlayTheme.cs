using Avalonia.Controls;
using Avalonia.Media;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The terminal palette resolved into the handful of brushes the overlays paint with.
///
/// Both overlays used to carry the same six brush fields, the same uint-to-brush
/// conversion and the same block of Fluent resource overrides; sharing one of these
/// keeps them in step and gives that TextBox styling a single home.
/// </summary>
sealed class OverlayTheme
{
    /// <param name="backgroundOpacity">
    /// Applies to <see cref="Background"/> alone, so an overlay that dims the terminal
    /// behind it and one that sits directly on top of it can share everything else.
    /// </param>
    public OverlayTheme(TerminalTheme theme, double backgroundOpacity = 1.0)
    {
        Background = Brush(theme.Background, backgroundOpacity);
        Surface = Brush(theme.Background);
        Foreground = Brush(theme.Foreground);
        Placeholder = Brush(theme.Foreground, 0.5);
        Dim = Brush(theme.Palette[8]); // Bright black / Surface2 — more readable
        Accent = Brush(theme.Palette[4]); // Blue
        Error = Brush(theme.Palette[1]); // Red
        Selection = Brush(theme.Selection);
    }

    /// <summary>Overlay backdrop, dimmed when the overlay was built with an opacity.</summary>
    public SolidColorBrush Background { get; }

    /// <summary>The same colour as <see cref="Background"/> but always opaque.</summary>
    public SolidColorBrush Surface { get; }

    public SolidColorBrush Foreground { get; }
    public SolidColorBrush Placeholder { get; }
    public SolidColorBrush Dim { get; }
    public SolidColorBrush Accent { get; }
    public SolidColorBrush Error { get; }
    public SolidColorBrush Selection { get; }

    public static SolidColorBrush Brush(uint color, double opacity = 1.0)
    {
        var c = Color.FromUInt32(color);
        if (opacity < 1.0)
        {
            c = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);
        }

        return new SolidColorBrush(c);
    }

    /// <summary>
    /// Paints a TextBox to match the overlay. Fluent styles the box through theme
    /// resources rather than properties, so each state has to be overridden by key or
    /// the stock chrome shows through on hover and focus.
    /// </summary>
    public void StyleTextBox(TextBox box)
    {
        box.Foreground = Foreground;
        box.CaretBrush = Accent;
        box.BorderBrush = Dim;

        foreach (var key in transparentKeys)
        {
            box.Resources[key] = Brushes.Transparent;
        }

        foreach (var key in foregroundKeys)
        {
            box.Resources[key] = Foreground;
        }
    }

    static readonly string[] transparentKeys =
    [
        "TextBoxBackground",
        "TextBoxBackgroundPointerOver",
        "TextBoxBackgroundFocused",
        "TextBoxBorderBrush",
        "TextBoxBorderBrushPointerOver",
        "TextBoxBorderBrushFocused",
        "TextControlBackground",
        "TextControlBackgroundPointerOver",
        "TextControlBackgroundFocused",
        "TextControlBorderBrush",
        "TextControlBorderBrushPointerOver",
        "TextControlBorderBrushFocused",
    ];

    static readonly string[] foregroundKeys =
    [
        "TextBoxForeground",
        "TextBoxForegroundPointerOver",
        "TextBoxForegroundFocused",
        "TextControlForeground",
        "TextControlForegroundPointerOver",
        "TextControlForegroundFocused",
    ];
}
