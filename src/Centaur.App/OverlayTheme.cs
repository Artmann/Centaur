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
        Accent = Brush(theme.Palette[4]); // Blue
        Error = Brush(theme.Palette[1]); // Red
        Selection = Brush(theme.Selection);

        // Nudged towards the foreground rather than taken from the palette, so one formula
        // works both ways round: on a light theme this darkens the card slightly against the
        // page, on a dark one it lightens it. A palette entry would only ever do one of those.
        Card = Blend(theme.Background, theme.Foreground, 0.06);
        Hairline = Blend(theme.Background, theme.Foreground, 0.16);

        // Everything below is solved for a contrast ratio against the card rather than blended
        // by a fixed amount, because no fixed amount works on all four palettes. A light theme
        // loses far less contrast per step away from its surface than a dark one gains, so an
        // amount tuned on Macchiato lands at roughly half the ratio on Latte. Solving states
        // the intent - "far enough off the card to see" - and lets each palette answer it.
        Hover = Solve(theme.Background, theme.Foreground, Card.Color, 1.12);
        Press = Solve(theme.Background, theme.Foreground, Card.Color, 1.28);
        Chip = Solve(theme.Background, theme.Foreground, Card.Color, ChipContrast);
        Edge = Solve(theme.Background, theme.Foreground, Card.Color, BoundaryContrast);
        Dim = Solve(theme.Background, theme.Foreground, Card.Color, MinimumContrast);
    }

    /// <summary>WCAG AA for body text. Everything painted in <see cref="Dim"/> is body text.</summary>
    public const double MinimumContrast = 4.5;

    /// <summary>What WCAG asks of a non-text element that identifies a control - the outline of
    /// a box, the ring of an unchosen radio, the focus ring.</summary>
    public const double BoundaryContrast = 3.0;

    /// <summary>Enough to see a chosen segment as chosen at a glance. Lower than a boundary
    /// because it is a fill behind text, not the outline of the control.</summary>
    public const double ChipContrast = 1.5;

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

    /// <summary>Fill for the settings page's grouped cards, a shade off the page behind them.</summary>
    public SolidColorBrush Card { get; }

    /// <summary>The one-pixel rule around a card and between the rows inside it.</summary>
    public SolidColorBrush Hairline { get; }

    /// <summary>What an interactive surface fills with while the pointer is over it.</summary>
    public SolidColorBrush Hover { get; }

    /// <summary>What it fills with while it is being pressed.</summary>
    public SolidColorBrush Press { get; }

    /// <summary>The fill that marks the chosen segment of a segmented control, or the open tab
    /// in the sidebar. Far enough off the card to be seen without the accent doing the work.</summary>
    public SolidColorBrush Chip { get; }

    /// <summary>The outline of a control - a box, a stepper, the ring of an unchosen radio.
    /// Distinct from <see cref="Hairline"/>, which rules a card and carries no meaning.</summary>
    public SolidColorBrush Edge { get; }

    public static SolidColorBrush Brush(uint color, double opacity = 1.0)
    {
        var c = Color.FromUInt32(color);
        if (opacity < 1.0)
        {
            c = Color.FromArgb((byte)(opacity * 255), c.R, c.G, c.B);
        }

        return new SolidColorBrush(c);
    }

    /// <summary>Mixes <paramref name="amount"/> of <paramref name="towards"/> into
    /// <paramref name="color"/>, opaquely - these sit over the terminal, so a translucent
    /// surface would show the panes through the settings page.</summary>
    static SolidColorBrush Blend(uint color, uint towards, double amount)
    {
        var from = Color.FromUInt32(color);
        var to = Color.FromUInt32(towards);

        return new SolidColorBrush(
            Color.FromRgb(Mix(from.R, to.R), Mix(from.G, to.G), Mix(from.B, to.B))
        );

        byte Mix(byte a, byte b) => (byte)Math.Round(a + ((b - a) * amount));
    }

    /// <summary>
    /// A hover or press fill for a surface that already carries a colour of its own - the filled
    /// half of a switch. <see cref="Hover"/> and <see cref="Press"/> are measured off the card, so
    /// on an accent fill they would replace the colour rather than react on top of it.
    /// </summary>
    public SolidColorBrush Shade(SolidColorBrush over, double amount) =>
        Blend(over.Color.ToUInt32(), Foreground.Color.ToUInt32(), amount);

    /// <summary>
    /// The smallest blend of <paramref name="color"/> towards <paramref name="towards"/> whose
    /// contrast against <paramref name="against"/> reaches <paramref name="target"/>.
    ///
    /// Contrast rises monotonically as the blend moves away from the surface it is measured on,
    /// so the first amount that passes is also the quietest one that does - which is what these
    /// colours want: as close to the surface as the requirement allows.
    /// </summary>
    static SolidColorBrush Solve(uint color, uint towards, Color against, double target)
    {
        for (var step = 10; step < 100; step++)
        {
            var candidate = Blend(color, towards, step / 100.0).Color;
            if (Contrast(candidate, against) >= target)
            {
                return new SolidColorBrush(candidate);
            }
        }

        return new SolidColorBrush(Color.FromUInt32(towards));
    }

    /// <summary>The WCAG 2.1 contrast ratio between two opaque colours, 1.0 to 21.0.</summary>
    public static double Contrast(Color a, Color b)
    {
        var (high, low) = (Luminance(a), Luminance(b));
        if (low > high)
        {
            (high, low) = (low, high);
        }

        return (high + 0.05) / (low + 0.05);
    }

    /// <summary>WCAG relative luminance: sRGB linearised, then weighted for the eye's response.</summary>
    static double Luminance(Color c) =>
        (0.2126 * Linear(c.R)) + (0.7152 * Linear(c.G)) + (0.0722 * Linear(c.B));

    static double Linear(byte channel)
    {
        var v = channel / 255.0;
        return v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Paints a TextBox to match the overlay. Fluent styles the box through theme
    /// resources rather than properties, so each state has to be overridden by key or
    /// the stock chrome shows through on hover and focus.
    /// </summary>
    public void StyleTextBox(TextBox box)
    {
        StyleTextBox(box, Brushes.Transparent, Foreground, Brushes.Transparent, Accent);

        // The overlays draw their own frame, so the property keeps a visible border even
        // though every border state resource is transparent.
        box.BorderBrush = Dim;

        // Fluent styles the watermark through a resource too, so without these the placeholder
        // keeps the stock grey and all but disappears against a light theme's surface.
        foreach (var key in placeholderKeys)
        {
            box.Resources[key] = Dim;
        }
    }

    /// <summary>
    /// The same resource-key override for a box that isn't part of an overlay and so
    /// carries its own palette — the tab strip's rename editor.
    /// </summary>
    public static void StyleTextBox(
        TextBox box,
        IBrush background,
        IBrush foreground,
        IBrush border,
        IBrush caret
    )
    {
        box.Background = background;
        box.Foreground = foreground;
        box.CaretBrush = caret;
        box.BorderBrush = border;

        foreach (var key in backgroundKeys)
        {
            box.Resources[key] = background;
        }

        foreach (var key in borderKeys)
        {
            box.Resources[key] = border;
        }

        foreach (var key in foregroundKeys)
        {
            box.Resources[key] = foreground;
        }
    }

    static readonly string[] backgroundKeys =
    [
        "TextBoxBackground",
        "TextBoxBackgroundPointerOver",
        "TextBoxBackgroundFocused",
        "TextControlBackground",
        "TextControlBackgroundPointerOver",
        "TextControlBackgroundFocused",
    ];

    static readonly string[] borderKeys =
    [
        "TextBoxBorderBrush",
        "TextBoxBorderBrushPointerOver",
        "TextBoxBorderBrushFocused",
        "TextControlBorderBrush",
        "TextControlBorderBrushPointerOver",
        "TextControlBorderBrushFocused",
    ];

    static readonly string[] placeholderKeys =
    [
        "TextControlPlaceholderForeground",
        "TextControlPlaceholderForegroundPointerOver",
        "TextControlPlaceholderForegroundFocused",
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
