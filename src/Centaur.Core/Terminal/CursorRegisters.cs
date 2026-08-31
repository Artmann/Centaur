namespace Centaur.Core.Terminal;

/// <summary>
/// The cursor saved by DECSC/SCP and put back by DECRC/RCP. There is one register per screen,
/// matching xterm: a full-screen app's save/restore on the alternate screen must not corrupt
/// the main screen's cursor, which is saved on 1049h and restored on 1049l. Which register a
/// call lands in follows the buffer it is given, not the mode flag, because the 1049 handler
/// saves before it switches and restores after it switches back.
///
/// What is saved is the position and the whole graphic rendition, as VT100 and xterm both
/// specify. Keeping only the colours would leave the style flags at whatever the pen reached
/// in between, so a program that turns inverse on between its save and its restore would get
/// it stuck on - and every space it printed afterwards would paint as a solid block of its
/// foreground colour.
/// </summary>
sealed class CursorRegisters
{
    struct SavedCursor
    {
        public int x;
        public int y;
        public Cell pen;
    }

    readonly ScreenBuffer alternateBuffer;
    readonly SgrPen pen;

    SavedCursor mainSaved;
    SavedCursor altSaved;

    public CursorRegisters(ScreenBuffer alternateBuffer, SgrPen pen)
    {
        this.alternateBuffer = alternateBuffer;
        this.pen = pen;

        // Both registers start at the pen's reset state, so a restore with nothing saved
        // homes the cursor and returns the theme's colours - not the transparent black an
        // unset Cell reference would give.
        var initial = new SavedCursor { pen = pen.Snapshot() };
        mainSaved = initial;
        altSaved = initial;
    }

    public void Save(ScreenBuffer buffer)
    {
        ref var slot = ref Slot(buffer);
        slot.x = buffer.cursorX;
        slot.y = buffer.cursorY;
        slot.pen = pen.Snapshot();
    }

    public void Restore(ScreenBuffer buffer)
    {
        ref var slot = ref Slot(buffer);
        buffer.cursorX = slot.x;
        buffer.cursorY = slot.y;
        pen.RestoreFrom(slot.pen);
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
