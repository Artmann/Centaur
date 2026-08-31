namespace Centaur.Core.Terminal;

/// <summary>
/// The cursor saved by DECSC/SCP and put back by DECRC/RCP. There is one register per screen,
/// matching xterm: a full-screen app's save/restore on the alternate screen must not corrupt
/// the main screen's cursor, which is saved on 1049h and restored on 1049l. Which register a
/// call lands in follows the buffer it is given, not the mode flag, because the 1049 handler
/// saves before it switches and restores after it switches back.
/// </summary>
sealed class CursorRegisters
{
    struct SavedCursor
    {
        public int x;
        public int y;
        public uint fg;
        public uint bg;
    }

    readonly ScreenBuffer alternateBuffer;
    readonly SgrPen pen;

    SavedCursor mainSaved;
    SavedCursor altSaved;

    public CursorRegisters(ScreenBuffer alternateBuffer, SgrPen pen)
    {
        this.alternateBuffer = alternateBuffer;
        this.pen = pen;
    }

    public void Save(ScreenBuffer buffer)
    {
        ref var slot = ref Slot(buffer);
        slot.x = buffer.cursorX;
        slot.y = buffer.cursorY;
        slot.fg = pen.Foreground;
        slot.bg = pen.Background;
    }

    public void Restore(ScreenBuffer buffer)
    {
        ref var slot = ref Slot(buffer);
        buffer.cursorX = slot.x;
        buffer.cursorY = slot.y;
        pen.Foreground = slot.fg;
        pen.Background = slot.bg;
    }

    ref SavedCursor Slot(ScreenBuffer buffer)
    {
        if (buffer == alternateBuffer)
        {
            return ref altSaved;
        }
        return ref mainSaved;
    }
}
