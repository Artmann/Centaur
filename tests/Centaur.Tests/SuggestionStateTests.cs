using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class SuggestionStateTests
{
    [Fact]
    public void Read_ReturnsDefault_BeforeAnyUpdate()
    {
        var state = new SuggestionState();

        var (text, col, row) = state.Read();

        Assert.Null(text);
        Assert.Equal(0, col);
        Assert.Equal(0, row);
    }

    [Fact]
    public void Update_And_Read_ReturnsValues()
    {
        var state = new SuggestionState();

        state.Update("hello", 5, 10);

        var (text, col, row) = state.Read();
        Assert.Equal("hello", text);
        Assert.Equal(5, col);
        Assert.Equal(10, row);
    }

    [Fact]
    public void Clear_SetsTextToNull()
    {
        var state = new SuggestionState();
        state.Update("hello", 5, 10);

        state.Clear();

        var (text, _, _) = state.Read();
        Assert.Null(text);
    }
}
