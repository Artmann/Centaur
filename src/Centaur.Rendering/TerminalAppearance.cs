using Centaur.Core.Terminal;

namespace Centaur.Rendering;

/// <summary>
/// The look of a pane, as the renderer needs it. Everything here is captured when the renderer
/// is built: the font metrics decide the cell size the whole grid is laid out on, so changing
/// any of it means a new renderer and a re-measure, not a mutation.
/// </summary>
/// <param name="FontSize">Glyph size in points; the cell width is measured from it.</param>
/// <param name="LineHeight">Cell height as a multiple of the font size.</param>
/// <param name="CursorStyle">Block, underline or bar.</param>
/// <param name="CursorBlink">Whether the cursor follows the 500ms blink phase.</param>
/// <param name="BackgroundOpacity">Alpha applied to the cleared background, for a see-through
/// window. 1 is fully opaque.</param>
public sealed record TerminalAppearance(
    float FontSize = 14f,
    float LineHeight = 1.2f,
    CursorStyle CursorStyle = CursorStyle.Block,
    bool CursorBlink = false,
    float BackgroundOpacity = 1f
)
{
    public static readonly TerminalAppearance Default = new();

    /// <summary>Reads the user's appearance settings. Everything is already clamped to a
    /// usable range by <see cref="Settings.Load"/>, so nothing is re-validated here.</summary>
    public static TerminalAppearance From(Settings settings) =>
        new(
            FontSize: (float)settings.FontSize,
            LineHeight: (float)settings.LineHeight,
            CursorStyle: settings.CursorStyle,
            CursorBlink: settings.CursorBlink,
            BackgroundOpacity: (float)settings.WindowOpacity
        );
}
