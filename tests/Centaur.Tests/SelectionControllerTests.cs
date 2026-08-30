using Centaur.App;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class SelectionControllerTests
{
    // Column 5 is the space, so "world" runs from column 6 to column 11.
    const string line = "hello world foo";

    static ScreenBuffer Grid()
    {
        var buffer = new ScreenBuffer(20, 3);
        buffer.WriteRow(0, line);
        buffer.WriteRow(1, line);
        return buffer;
    }

    // --- click granularity ---

    [Fact]
    public void SingleClick_SelectsNothingUntilThePointerMoves()
    {
        var selection = new SelectionController();

        selection.BeginDrag(Grid(), 3, 0, clickCount: 1);

        Assert.False(selection.HasSelection);
        Assert.True(selection.IsDragging);
        Assert.Null(selection.Normalized);
    }

    [Fact]
    public void DoubleClick_SelectsTheWordUnderTheCursor()
    {
        var selection = new SelectionController();

        selection.BeginDrag(Grid(), 8, 0, clickCount: 2);

        Assert.True(selection.HasSelection);
        Assert.Equal(new(6, 0, 11, 0), selection.Current);
    }

    [Fact]
    public void TripleClick_SelectsTheWholeLine()
    {
        var selection = new SelectionController();

        selection.BeginDrag(Grid(), 8, 1, clickCount: 3);

        Assert.Equal(new(0, 1, 20, 1), selection.Current);
    }

    [Fact]
    public void MoreThanThreeClicks_StillSelectsTheWholeLine()
    {
        var selection = new SelectionController();

        selection.BeginDrag(Grid(), 8, 1, clickCount: 5);

        Assert.Equal(new(0, 1, 20, 1), selection.Current);
    }

    // --- character drags ---

    [Fact]
    public void CharacterDrag_ToAnotherCell_Selects()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 3, 0, clickCount: 1);
        selection.ExtendDrag(buffer, 7, 1);

        Assert.True(selection.HasSelection);
        Assert.Equal(new(3, 0, 7, 1), selection.Current);
    }

    [Fact]
    public void CharacterDrag_Backwards_NormalizesIntoReadingOrder()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 7, 1, clickCount: 1);
        selection.ExtendDrag(buffer, 3, 0);

        Assert.Equal(new(3, 0, 7, 1), selection.Current);
    }

    [Fact]
    public void CharacterDrag_BackToTheStartingCell_DeselectsOnRelease()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 3, 0, clickCount: 1);
        selection.ExtendDrag(buffer, 7, 0);
        selection.ExtendDrag(buffer, 3, 0);
        selection.EndDrag(3, 0);

        Assert.False(selection.HasSelection);
        Assert.False(selection.IsDragging);
    }

    [Fact]
    public void CharacterDrag_ReleasedElsewhere_KeepsTheSelection()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 3, 0, clickCount: 1);
        selection.ExtendDrag(buffer, 7, 0);
        selection.EndDrag(7, 0);

        Assert.True(selection.HasSelection);
        Assert.Equal(new(3, 0, 7, 0), selection.Current);
    }

    // --- word drags ---

    [Fact]
    public void WordDrag_Forwards_ExtendsToTheFarWordsEnd()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        // Start on "world", drag into "foo" (columns 12-14).
        selection.BeginDrag(buffer, 8, 0, clickCount: 2);
        selection.ExtendDrag(buffer, 13, 0);

        Assert.Equal(new(6, 0, 15, 0), selection.Current);
    }

    [Fact]
    public void WordDrag_Backwards_PivotsAroundTheFarEndOfTheAnchorWord()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        // Start on "world", drag back into "hello": the anchor flips to the end of
        // "world" so the whole anchor word stays inside the selection.
        selection.BeginDrag(buffer, 8, 0, clickCount: 2);
        selection.ExtendDrag(buffer, 2, 0);

        Assert.Equal(new(0, 0, 11, 0), selection.Current);
    }

    [Fact]
    public void WordDrag_ToTheNextRow_ExtendsDownwards()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 8, 0, clickCount: 2);
        selection.ExtendDrag(buffer, 2, 1);

        Assert.Equal(new(6, 0, 5, 1), selection.Current);
    }

    // --- line drags ---

    [Fact]
    public void LineDrag_Downwards_CoversBothLines()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 4, 0, clickCount: 3);
        selection.ExtendDrag(buffer, 4, 1);

        Assert.Equal(new(0, 0, 20, 1), selection.Current);
    }

    [Fact]
    public void LineDrag_Upwards_FlipsTheEndpointsSoTheRunStillReadsForwards()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 4, 1, clickCount: 3);
        selection.ExtendDrag(buffer, 4, 0);

        Assert.Equal(new(0, 0, 20, 1), selection.Current);
    }

    [Fact]
    public void LineDrag_ReleasedOnTheStartingCell_KeepsTheLineSelected()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 4, 0, clickCount: 3);
        selection.EndDrag(4, 0);

        Assert.True(selection.HasSelection);
    }

    // --- clearing ---

    [Fact]
    public void Clear_DropsTheSelection()
    {
        var buffer = Grid();
        var selection = new SelectionController();

        selection.BeginDrag(buffer, 8, 0, clickCount: 2);
        selection.Clear();

        Assert.False(selection.HasSelection);
        Assert.Null(selection.Normalized);
    }
}
