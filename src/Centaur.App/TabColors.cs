using Avalonia.Media;

namespace Centaur.App;

/// <summary>The tab strip's palette, shared by <see cref="TabBar"/> and its drag controller.</summary>
static class TabColors
{
    public static readonly IBrush activeBg = SolidColorBrush.Parse("#363A4F");
    public static readonly IBrush inactiveBg = Brushes.Transparent;
    public static readonly IBrush hoverBg = SolidColorBrush.Parse("#2E3248");
    public static readonly IBrush activeText = SolidColorBrush.Parse("#CAD3F5");
    public static readonly IBrush inactiveText = SolidColorBrush.Parse("#7F849C");
    public static readonly IBrush closeHoverBg = SolidColorBrush.Parse("#ED8796");
    public static readonly IBrush closeHoverText = SolidColorBrush.Parse("#24273A");
    public static readonly IBrush editorBg = SolidColorBrush.Parse("#24273A");
    public static readonly IBrush editorBorder = SolidColorBrush.Parse("#494D64");
    public static readonly IBrush editorSelection = SolidColorBrush.Parse("#5B6078");
    public static readonly IBrush dropIndicator = SolidColorBrush.Parse("#8AADF4");
}
