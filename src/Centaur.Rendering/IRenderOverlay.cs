using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using SkiaSharp;

namespace Centaur.Rendering;

public interface IRenderOverlay : IProvider
{
    void Render(
        SKCanvas canvas,
        float canvasWidth,
        TerminalTheme theme,
        SKFont baseFont,
        SKTypeface typeface
    );
}
