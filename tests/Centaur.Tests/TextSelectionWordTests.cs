using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>Double-click word selection: where a word starts and ends around a given cell.</summary>
public class TextSelectionWordTests
{
    // --- FindWordStart / FindWordEnd ---

    [Fact]
    public void FindWordStart_MiddleOfWord_ReturnsWordStart()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello world foo");

        Assert.Equal(6, TextSelection.FindWordStart(buffer, 8, 0));
    }

    [Fact]
    public void FindWordEnd_MiddleOfWord_ReturnsOnePastEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello world foo");

        Assert.Equal(11, TextSelection.FindWordEnd(buffer, 8, 0));
    }

    [Fact]
    public void FindWordStart_AtWordStart_ReturnsSamePosition()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello world");

        Assert.Equal(6, TextSelection.FindWordStart(buffer, 6, 0));
    }

    [Fact]
    public void FindWordEnd_AtLastCharOfWord_ReturnsOnePastEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello world");

        Assert.Equal(11, TextSelection.FindWordEnd(buffer, 10, 0));
    }

    [Fact]
    public void FindWordStart_FirstWordInLine_ReturnsZero()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello world");

        Assert.Equal(0, TextSelection.FindWordStart(buffer, 2, 0));
    }

    [Fact]
    public void FindWordEnd_LastWordInLine_ReturnsColumnCount()
    {
        // "foo" at end fills cols 17,18,19 in a 20-col buffer; rest is spaces
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(17, 0, "foo");

        Assert.Equal(20, TextSelection.FindWordEnd(buffer, 18, 0));
    }

    [Fact]
    public void FindWordStart_OnSpace_ReturnsSpaceRunStart()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello   world");
        // spaces at 5,6,7 — clicking col 6 should select the space run

        Assert.Equal(5, TextSelection.FindWordStart(buffer, 6, 0));
    }

    [Fact]
    public void FindWordEnd_OnSpace_ReturnsSpaceRunEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "hello   world");

        Assert.Equal(8, TextSelection.FindWordEnd(buffer, 6, 0));
    }

    [Fact]
    public void FindWordBoundaries_UnderscoreIsWordChar()
    {
        var buffer = new ScreenBuffer(20, 1);
        buffer.WriteAt(0, 0, "my_var = 1");

        Assert.Equal(0, TextSelection.FindWordStart(buffer, 3, 0));
        Assert.Equal(6, TextSelection.FindWordEnd(buffer, 3, 0));
    }
}
