using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

public class TextSelectionTests
{
    // --- Normalize ---

    [Fact]
    public void Normalize_StartBeforeEnd_ReturnsAsIs()
    {
        var sel = TextSelection.Normalize(2, 1, 5, 3);

        Assert.Equal(2, sel.StartColumn);
        Assert.Equal(1, sel.StartRow);
        Assert.Equal(5, sel.EndColumn);
        Assert.Equal(3, sel.EndRow);
    }

    [Fact]
    public void Normalize_EndBeforeStart_Swaps()
    {
        var sel = TextSelection.Normalize(5, 3, 2, 1);

        Assert.Equal(2, sel.StartColumn);
        Assert.Equal(1, sel.StartRow);
        Assert.Equal(5, sel.EndColumn);
        Assert.Equal(3, sel.EndRow);
    }

    [Fact]
    public void Normalize_SameRow_EndColBeforeStartCol_SwapsColumns()
    {
        var sel = TextSelection.Normalize(7, 2, 3, 2);

        Assert.Equal(3, sel.StartColumn);
        Assert.Equal(2, sel.StartRow);
        Assert.Equal(7, sel.EndColumn);
        Assert.Equal(2, sel.EndRow);
    }

    [Fact]
    public void Normalize_SamePosition_ReturnsEqual()
    {
        var sel = TextSelection.Normalize(4, 2, 4, 2);

        Assert.Equal(4, sel.StartColumn);
        Assert.Equal(2, sel.StartRow);
        Assert.Equal(4, sel.EndColumn);
        Assert.Equal(2, sel.EndRow);
    }

    // --- IsInSelection ---

    [Fact]
    public void IsInSelection_CellAboveRange_ReturnsFalse()
    {
        var sel = new TextSelection(2, 2, 5, 4);
        Assert.False(TextSelection.IsInSelection(3, 1, sel));
    }

    [Fact]
    public void IsInSelection_CellBelowRange_ReturnsFalse()
    {
        var sel = new TextSelection(2, 2, 5, 4);
        Assert.False(TextSelection.IsInSelection(3, 5, sel));
    }

    [Fact]
    public void IsInSelection_SingleRow_InRange_ReturnsTrue()
    {
        var sel = new TextSelection(2, 3, 6, 3);
        Assert.True(TextSelection.IsInSelection(3, 3, sel));
    }

    [Fact]
    public void IsInSelection_SingleRow_BeforeStart_ReturnsFalse()
    {
        var sel = new TextSelection(2, 3, 6, 3);
        Assert.False(TextSelection.IsInSelection(1, 3, sel));
    }

    [Fact]
    public void IsInSelection_SingleRow_AtEnd_ReturnsFalse()
    {
        var sel = new TextSelection(2, 3, 6, 3);
        Assert.False(TextSelection.IsInSelection(6, 3, sel));
    }

    [Fact]
    public void IsInSelection_StartRow_AtStartCol_ReturnsTrue()
    {
        var sel = new TextSelection(3, 1, 5, 3);
        Assert.True(TextSelection.IsInSelection(3, 1, sel));
    }

    [Fact]
    public void IsInSelection_StartRow_BeforeStartCol_ReturnsFalse()
    {
        var sel = new TextSelection(3, 1, 5, 3);
        Assert.False(TextSelection.IsInSelection(2, 1, sel));
    }

    [Fact]
    public void IsInSelection_EndRow_BeforeEndCol_ReturnsTrue()
    {
        var sel = new TextSelection(3, 1, 5, 3);
        Assert.True(TextSelection.IsInSelection(4, 3, sel));
    }

    [Fact]
    public void IsInSelection_EndRow_AtEndCol_ReturnsFalse()
    {
        var sel = new TextSelection(3, 1, 5, 3);
        Assert.False(TextSelection.IsInSelection(5, 3, sel));
    }

    [Fact]
    public void IsInSelection_MiddleRow_ReturnsTrue()
    {
        var sel = new TextSelection(3, 1, 5, 3);
        Assert.True(TextSelection.IsInSelection(0, 2, sel));
    }

    // --- FindWordStart / FindWordEnd ---

    [Fact]
    public void FindWordStart_MiddleOfWord_ReturnsWordStart()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello world foo");

        Assert.Equal(6, TextSelection.FindWordStart(buffer, 8, 0));
    }

    [Fact]
    public void FindWordEnd_MiddleOfWord_ReturnsOnePastEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello world foo");

        Assert.Equal(11, TextSelection.FindWordEnd(buffer, 8, 0));
    }

    [Fact]
    public void FindWordStart_AtWordStart_ReturnsSamePosition()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello world");

        Assert.Equal(6, TextSelection.FindWordStart(buffer, 6, 0));
    }

    [Fact]
    public void FindWordEnd_AtLastCharOfWord_ReturnsOnePastEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello world");

        Assert.Equal(11, TextSelection.FindWordEnd(buffer, 10, 0));
    }

    [Fact]
    public void FindWordStart_FirstWordInLine_ReturnsZero()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello world");

        Assert.Equal(0, TextSelection.FindWordStart(buffer, 2, 0));
    }

    [Fact]
    public void FindWordEnd_LastWordInLine_ReturnsColumnCount()
    {
        // "foo" at end fills cols 17,18,19 in a 20-col buffer; rest is spaces
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 17, 0, "foo");

        Assert.Equal(20, TextSelection.FindWordEnd(buffer, 18, 0));
    }

    [Fact]
    public void FindWordStart_OnSpace_ReturnsSpaceRunStart()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello   world");
        // spaces at 5,6,7 — clicking col 6 should select the space run

        Assert.Equal(5, TextSelection.FindWordStart(buffer, 6, 0));
    }

    [Fact]
    public void FindWordEnd_OnSpace_ReturnsSpaceRunEnd()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "hello   world");

        Assert.Equal(8, TextSelection.FindWordEnd(buffer, 6, 0));
    }

    [Fact]
    public void FindWordBoundaries_UnderscoreIsWordChar()
    {
        var buffer = new ScreenBuffer(20, 1);
        WriteString(buffer, 0, 0, "my_var = 1");

        Assert.Equal(0, TextSelection.FindWordStart(buffer, 3, 0));
        Assert.Equal(6, TextSelection.FindWordEnd(buffer, 3, 0));
    }

    // --- ExtractText ---

    [Fact]
    public void ExtractText_SingleRow_ReturnsSubstring()
    {
        var buffer = new ScreenBuffer(10, 3);
        WriteString(buffer, 0, 0, "Hello World");

        var sel = new TextSelection(0, 0, 5, 0);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("Hello", text);
    }

    [Fact]
    public void ExtractText_MultiRow_JoinsWithNewline()
    {
        var buffer = new ScreenBuffer(10, 3);
        WriteString(buffer, 0, 0, "AAAAAAAAAA");
        WriteString(buffer, 0, 1, "BBBBBBBBBB");
        WriteString(buffer, 0, 2, "CCCCCCCCCC");

        var sel = new TextSelection(5, 0, 3, 2);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("AAAAA\nBBBBBBBBBB\nCCC", text);
    }

    [Fact]
    public void ExtractText_TrimsTrailingSpaces()
    {
        var buffer = new ScreenBuffer(10, 3);
        WriteString(buffer, 0, 0, "Hi");
        WriteString(buffer, 0, 1, "There");

        var sel = new TextSelection(0, 0, 5, 1);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("Hi\nThere", text);
    }

    [Fact]
    public void ExtractText_EmptySelection_ReturnsEmpty()
    {
        var buffer = new ScreenBuffer(10, 3);
        WriteString(buffer, 0, 0, "Hello");

        var sel = new TextSelection(3, 0, 3, 0);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("", text);
    }

    static void WriteString(ScreenBuffer buffer, int startX, int row, string text)
    {
        buffer.cursorX = startX;
        buffer.cursorY = row;
        foreach (var c in text)
            buffer.Write(c);
    }
}
