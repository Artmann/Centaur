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
        Assert.Equal(1, buffer.cursorX);  // Cursor advances after writing
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
}
