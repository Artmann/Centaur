using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Resize must preserve per-row OSC 133 semantic-prompt marks for the rows that
/// survive the resize, mirroring how cell content is copied. Dropping them would
/// break jump-to-prompt navigation after a window resize.
/// </summary>
public class ScreenBufferResizeTests
{
    static ScreenBuffer NewBuffer(int columns, int rows) =>
        new(columns, rows, CatppuccinThemes.Macchiato);

    [Fact]
    public void Resize_PreservesMarksForOverlappingRows()
    {
        var buffer = NewBuffer(80, 24);
        buffer.SetMark(2, PromptMark.Prompt);
        buffer.SetMark(5, PromptMark.Command);

        buffer.Resize(100, 30);

        Assert.Equal(PromptMark.Prompt, buffer.GetMark(2));
        Assert.Equal(PromptMark.Command, buffer.GetMark(5));
    }

    [Fact]
    public void Resize_NewRows_DefaultToNone()
    {
        var buffer = NewBuffer(80, 24);
        buffer.SetMark(10, PromptMark.Output);

        buffer.Resize(80, 40);

        Assert.Equal(PromptMark.None, buffer.GetMark(30));
    }

    [Fact]
    public void Resize_Smaller_DropsMarksBeyondNewBounds()
    {
        var buffer = NewBuffer(80, 24);
        buffer.SetMark(3, PromptMark.Prompt);
        buffer.SetMark(20, PromptMark.Command);

        buffer.Resize(80, 10);

        Assert.Equal(PromptMark.Prompt, buffer.GetMark(3));
        Assert.Equal(PromptMark.None, buffer.GetMark(20));
    }
}
