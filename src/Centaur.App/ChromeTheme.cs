using Avalonia.Media;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// The window's own furniture - title bar, tab strip, caption buttons, context menus - painted
/// from the active <see cref="TerminalTheme"/> instead of the hardcoded Macchiato hex it used
/// to carry, so switching theme does not leave the chrome behind.
///
/// Every brush is created once and then <em>mutated in place</em>. A control that has already
/// taken one of these repaints when its colour changes, which handing out a fresh brush would
/// not do - and it means the XAML styles can bind to them without any per-theme rebinding.
/// </summary>
static class ChromeTheme
{
    /// <summary>The window background.</summary>
    public static SolidColorBrush Base { get; } = new();

    /// <summary>The raised surface: the active tab, menu backgrounds, hovered caption buttons.</summary>
    public static SolidColorBrush Surface { get; } = new();

    /// <summary>Halfway between <see cref="Base"/> and <see cref="Surface"/>, for tab hover.</summary>
    public static SolidColorBrush SurfaceHover { get; } = new();

    /// <summary>Borders, separators and the pressed state of a caption button.</summary>
    public static SolidColorBrush Border { get; } = new();

    public static SolidColorBrush Foreground { get; } = new();

    /// <summary>Text and glyphs that are present but not the subject: inactive tabs, the
    /// caption button strokes.</summary>
    public static SolidColorBrush Dim { get; } = new();

    public static SolidColorBrush Accent { get; } = new();

    /// <summary>The close button's hover fill, and anything else destructive.</summary>
    public static SolidColorBrush Danger { get; } = new();

    /// <summary>The close button held down - <see cref="Danger"/> lifted towards the text
    /// colour, so the press reads on a light theme as well as a dark one.</summary>
    public static SolidColorBrush DangerPressed { get; } = new();

    /// <summary>Selected text inside chrome controls, such as the tab rename box.</summary>
    public static SolidColorBrush Selection { get; } = new();

    static ChromeTheme()
    {
        Apply(CatppuccinThemes.Macchiato);
    }

    /// <summary>Repaints every chrome brush from a terminal theme. Safe to call at any time;
    /// the controls already holding these brushes follow along.</summary>
    public static void Apply(TerminalTheme theme)
    {
        Base.Color = Rgb(theme.Background);
        Surface.Color = Rgb(theme.Selection);
        SurfaceHover.Color = Mix(theme.Background, theme.Selection, 0.5);
        Border.Color = Rgb(theme.Palette[0]);
        Foreground.Color = Rgb(theme.Foreground);
        Dim.Color = Mix(theme.Background, theme.Foreground, 0.55);
        Accent.Color = Rgb(theme.Palette[4]);
        Danger.Color = Rgb(theme.Palette[1]);
        DangerPressed.Color = Mix(theme.Palette[1], theme.Foreground, 0.15);
        Selection.Color = Rgb(theme.Palette[8]);
    }

    static Color Rgb(uint color)
    {
        var c = Color.FromUInt32(color);
        return Color.FromRgb(c.R, c.G, c.B);
    }

    /// <summary>Blends <paramref name="to"/> into <paramref name="from"/> by
    /// <paramref name="amount"/>, so a derived shade tracks the theme instead of being a
    /// second hardcoded colour.</summary>
    static Color Mix(uint from, uint to, double amount)
    {
        var a = Color.FromUInt32(from);
        var b = Color.FromUInt32(to);
        return Color.FromRgb(
            Channel(a.R, b.R, amount),
            Channel(a.G, b.G, amount),
            Channel(a.B, b.B, amount)
        );
    }

    static byte Channel(byte from, byte to, double amount) =>
        (byte)Math.Clamp(from + (to - from) * amount, 0, 255);
}
