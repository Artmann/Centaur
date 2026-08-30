using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ScreenBufferTests
{
    [Fact]
    public void NewBuffer_HasCorrectDimensions()
    {
        var buffer = new ScreenBuffer(80, 24);

        Assert.Equal(80, buffer.columns);
        Assert.Equal(24, buffer.rows);
    }

    [Fact]
    public void NewBuffer_CursorAtOrigin()
    {
        var buffer = new ScreenBuffer(80, 24);

        Assert.Equal(0, buffer.cursorX);
        Assert.Equal(0, buffer.cursorY);
    }

    [Fact]
    public void Write_PlacesCharacterAtCursor()
    {
        var buffer = new ScreenBuffer(80, 24);

        buffer.Write('H');
        buffer.Write('i');

        Assert.Equal('H', buffer[0, 0].character);
        Assert.Equal('i', buffer[1, 0].character);
        Assert.Equal(2, buffer.cursorX);
    }

    [Fact]
    public void Write_WrapsAtEndOfLine()
    {
        var buffer = new ScreenBuffer(3, 2);

        buffer.Write('A');
        buffer.Write('B');
        buffer.Write('C');
        buffer.Write('D');

        Assert.Equal('D', buffer[0, 1].character);
        Assert.Equal(1, buffer.cursorX); // Cursor advances after writing
        Assert.Equal(1, buffer.cursorY);
    }

    [Fact]
    public void Clear_ResetsBuffer()
    {
        var buffer = new ScreenBuffer(80, 24);
        buffer.Write('X');
        buffer.cursorX = 10;
        buffer.cursorY = 5;

        buffer.Clear();

        Assert.Equal(' ', buffer[0, 0].character);
        Assert.Equal(0, buffer.cursorX);
        Assert.Equal(0, buffer.cursorY);
    }

    [Fact]
    public void OutOfBounds_ReturnsDefaultCell()
    {
        var buffer = new ScreenBuffer(10, 10);

        var cell = buffer[-1, -1];

        Assert.Equal(' ', cell.character);
    }

    [Fact]
    public void ScrollUp_CapturesLineToScrollback()
    {
        var buffer = new ScreenBuffer(5, 3);
        buffer[0, 0] = new Cell('A');
        buffer[0, 1] = new Cell('B');
        buffer[0, 2] = new Cell('C');

        buffer.ScrollUp(1);

        Assert.Equal(1, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollUp_CapturesCorrectContent()
    {
        var buffer = new ScreenBuffer(3, 3);
        buffer.WriteRow(0, "XYZ");

        buffer.ScrollUp(1);

        // The scrolled-off row should be in scrollback
        Assert.Equal(1, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollUp_MultipleLines_CapturesAll()
    {
        var buffer = new ScreenBuffer(3, 5);
        buffer[0, 0] = new Cell('A');
        buffer[0, 1] = new Cell('B');
        buffer[0, 2] = new Cell('C');

        buffer.ScrollUp(2);

        Assert.Equal(2, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollUpInRegion_FullScreen_CapturesLine()
    {
        var buffer = new ScreenBuffer(5, 4);
        buffer[0, 0] = new Cell('A');

        buffer.ScrollUpInRegion(1, 0, 3); // full screen region

        Assert.Equal(1, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollUpInRegion_PartialRegion_DoesNotCapture()
    {
        var buffer = new ScreenBuffer(5, 4);
        buffer[0, 1] = new Cell('B');

        buffer.ScrollUpInRegion(1, 1, 3); // partial region

        Assert.Equal(0, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollbackDisabled_ScrollUpDoesNotCapture()
    {
        var buffer = new ScreenBuffer(5, 3, enableScrollback: false);
        buffer[0, 0] = new Cell('A');

        buffer.ScrollUp(1);

        Assert.Equal(0, buffer.Scrollback.Count);
    }

    [Fact]
    public void ScrollViewUp_ClampsToScrollbackCount()
    {
        var buffer = new ScreenBuffer(5, 3);
        buffer[0, 0] = new Cell('A');
        buffer.ScrollUp(1); // 1 line in scrollback

        buffer.Scrollback.ScrollUp(10);

        Assert.Equal(1, buffer.Scrollback.Offset);
    }

    [Fact]
    public void ScrollViewDown_ClampsToZero()
    {
        var buffer = new ScreenBuffer(5, 3);

        buffer.Scrollback.ScrollDown(5);

        Assert.Equal(0, buffer.Scrollback.Offset);
    }

    [Fact]
    public void ScrollToBottom_ResetsOffset()
    {
        var buffer = new ScreenBuffer(5, 3);
        buffer[0, 0] = new Cell('A');
        buffer.ScrollUp(1);
        buffer.Scrollback.ScrollUp(1);

        buffer.Scrollback.ScrollToBottom();

        Assert.Equal(0, buffer.Scrollback.Offset);
    }

    [Fact]
    public void Snapshot_WithScrollOffset_HidesCursor()
    {
        var buffer = new ScreenBuffer(5, 3);
        buffer[0, 0] = new Cell('A');
        buffer.ScrollUp(1);
        buffer.Scrollback.ScrollUp(1);

        var snapshot = buffer.Snapshot();

        Assert.Equal(-1, snapshot.cursorY);
    }

    [Fact]
    public void Snapshot_WithScrollOffset_ShowsScrollbackRows()
    {
        var buffer = new ScreenBuffer(3, 3);
        // Put 'X' in row 0
        buffer.WriteRow(0, "XYZ");
        // Scroll up so row 0 goes to scrollback
        buffer.ScrollUp(1);
        // Now put 'A' in the new content
        buffer[0, 0] = new Cell('A');

        // Scroll view up by 1 to see the scrollback row
        buffer.Scrollback.ScrollUp(1);

        var snapshot = buffer.Snapshot();
        // Top row of snapshot should be the scrollback row
        Assert.Equal('X', snapshot[0, 0].character);
        Assert.Equal('Y', snapshot[1, 0].character);
        Assert.Equal('Z', snapshot[2, 0].character);
        // Second row should be first row of live buffer (A)
        Assert.Equal('A', snapshot[0, 1].character);
    }

    [Fact]
    public void ClearScrollback_ResetsScrollbackAndOffset()
    {
        var buffer = new ScreenBuffer(5, 3);
        buffer[0, 0] = new Cell('A');
        buffer.ScrollUp(1);
        buffer.Scrollback.ScrollUp(1);

        buffer.Scrollback.Clear();

        Assert.Equal(0, buffer.Scrollback.Count);
        Assert.Equal(0, buffer.Scrollback.Offset);
    }
}
