using SkiaSharp;

namespace Centaur.Rendering;

/// <summary>
/// Per-frame scratch arrays for glyph collection and colour-batched drawing. Sized to one
/// grid's worth of cells and grown in place, so a steady-state frame allocates nothing.
/// Owned by <see cref="TerminalRenderer"/> and only ever touched on the render thread.
/// </summary>
internal sealed class GlyphRunBuffers
{
    // Filled by the collect pass, one entry per visible glyph. The font is the (typeface, bold,
    // italic) variant the glyph is drawn with, so it also carries the cell's SGR styling.
    public ushort[] glyphs = [];
    public SKPoint[] positions = [];
    public uint[] colors = [];
    public SKFont[] fonts = [];

    // Marks the glyphs already emitted, so the colour-batching pass visits each once.
    public bool[] drawn = [];

    // The run currently being assembled for a single DrawText call.
    public ushort[] runGlyphs = [];
    public SKPoint[] runPositions = [];

    int capacity;

    public void Ensure(int cellCount)
    {
        if (capacity >= cellCount)
        {
            return;
        }

        capacity = cellCount;
        glyphs = new ushort[cellCount];
        positions = new SKPoint[cellCount];
        colors = new uint[cellCount];
        drawn = new bool[cellCount];
        fonts = new SKFont[cellCount];
        runGlyphs = new ushort[cellCount];
        runPositions = new SKPoint[cellCount];
    }
}
