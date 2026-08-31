using Avalonia.Media;

namespace Centaur.App;

/// <summary>
/// The tab strip's palette, shared by <see cref="TabBar"/> and its drag controller. Every entry
/// is one of <see cref="ChromeTheme"/>'s brushes under the name the tab strip uses for it, so a
/// theme change repaints the strip without the strip knowing a theme exists.
/// </summary>
static class TabColors
{
    public static readonly IBrush activeBg = ChromeTheme.Surface;
    public static readonly IBrush inactiveBg = Brushes.Transparent;
    public static readonly IBrush hoverBg = ChromeTheme.SurfaceHover;
    public static readonly IBrush activeText = ChromeTheme.Foreground;
    public static readonly IBrush inactiveText = ChromeTheme.Dim;
    public static readonly IBrush closeHoverBg = ChromeTheme.Danger;
    public static readonly IBrush closeHoverText = ChromeTheme.Base;
    public static readonly IBrush editorBg = ChromeTheme.Base;
    public static readonly IBrush editorBorder = ChromeTheme.Border;
    public static readonly IBrush editorSelection = ChromeTheme.Selection;
    public static readonly IBrush dropIndicator = ChromeTheme.Accent;
}
