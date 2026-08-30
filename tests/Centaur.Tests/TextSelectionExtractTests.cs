using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>Turning a selected range back into the text that goes on the clipboard.</summary>
public class TextSelectionExtractTests
{
    // --- ExtractText ---

    [Fact]
    public void ExtractText_SingleRow_ReturnsSubstring()
    {
        var buffer = new ScreenBuffer(10, 3);
        buffer.WriteAt(0, 0, "Hello World");

        var sel = new TextSelection(0, 0, 5, 0);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("Hello", text);
    }

    [Fact]
    public void ExtractText_MultiRow_JoinsWithNewline()
    {
        var buffer = new ScreenBuffer(10, 3);
        buffer.WriteAt(0, 0, "AAAAAAAAAA");
        buffer.WriteAt(0, 1, "BBBBBBBBBB");
        buffer.WriteAt(0, 2, "CCCCCCCCCC");

        var sel = new TextSelection(5, 0, 3, 2);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("AAAAA\nBBBBBBBBBB\nCCC", text);
    }

    [Fact]
    public void ExtractText_TrimsTrailingSpaces()
    {
        var buffer = new ScreenBuffer(10, 3);
        buffer.WriteAt(0, 0, "Hi");
        buffer.WriteAt(0, 1, "There");

        var sel = new TextSelection(0, 0, 5, 1);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("Hi\nThere", text);
    }

    [Fact]
    public void ExtractText_EmptySelection_ReturnsEmpty()
    {
        var buffer = new ScreenBuffer(10, 3);
        buffer.WriteAt(0, 0, "Hello");

        var sel = new TextSelection(3, 0, 3, 0);
        var text = TextSelection.ExtractText(buffer, sel);

        Assert.Equal("", text);
    }
}
