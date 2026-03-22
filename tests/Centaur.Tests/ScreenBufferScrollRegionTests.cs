using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ScreenBufferScrollRegionTests
{
    readonly ScreenBuffer buffer;

    public ScreenBufferScrollRegionTests()
    {
        buffer = new ScreenBuffer(10, 10);
    }

    [Fact]
    public void DefaultScrollRegion_IsFullScreen()
    {
        Assert.Equal(0, buffer.scrollTop);
        Assert.Equal(9, buffer.scrollBottom);
    }

    [Fact]
    public void SetScrollRegion_SetsTopAndBottom()
    {
        buffer.SetScrollRegion(2, 7);

        Assert.Equal(2, buffer.scrollTop);
        Assert.Equal(7, buffer.scrollBottom);
    }

    [Fact]
    public void SetScrollRegion_ClampsToValidRange()
    {
        buffer.SetScrollRegion(-5, 100);

        Assert.Equal(0, buffer.scrollTop);
        Assert.Equal(9, buffer.scrollBottom);
    }

    [Fact]
    public void SetScrollRegion_InvalidRange_ResetsToFullScreen()
    {
        buffer.SetScrollRegion(5, 2);

        Assert.Equal(0, buffer.scrollTop);
        Assert.Equal(9, buffer.scrollBottom);
    }

    [Fact]
    public void ScrollUpInRegion_OnlyAffectsRegionRows()
    {
        // Fill rows with identifiable characters
        for (int y = 0; y < 10; y++)
        {
            buffer[0, y] = new Cell((char)('A' + y));
        }

        // Scroll within region 3..6 (rows 3,4,5,6)
        buffer.ScrollUpInRegion(1, 3, 6);

        // Rows outside region unchanged
        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[0, 1].character);
        Assert.Equal('C', buffer[0, 2].character);
        // Region shifted up
        Assert.Equal('E', buffer[0, 3].character); // was row 4
        Assert.Equal('F', buffer[0, 4].character); // was row 5
        Assert.Equal('G', buffer[0, 5].character); // was row 6
        Assert.Equal(' ', buffer[0, 6].character); // cleared
        // Rows after region unchanged
        Assert.Equal('H', buffer[0, 7].character);
        Assert.Equal('I', buffer[0, 8].character);
        Assert.Equal('J', buffer[0, 9].character);
    }

    [Fact]
    public void ScrollDownInRegion_OnlyAffectsRegionRows()
    {
        for (int y = 0; y < 10; y++)
        {
            buffer[0, y] = new Cell((char)('A' + y));
        }

        buffer.ScrollDownInRegion(1, 3, 6);

        // Rows outside region unchanged
        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[0, 1].character);
        Assert.Equal('C', buffer[0, 2].character);
        // Region shifted down
        Assert.Equal(' ', buffer[0, 3].character); // cleared
        Assert.Equal('D', buffer[0, 4].character); // was row 3
        Assert.Equal('E', buffer[0, 5].character); // was row 4
        Assert.Equal('F', buffer[0, 6].character); // was row 5
        // Rows after region unchanged
        Assert.Equal('H', buffer[0, 7].character);
        Assert.Equal('I', buffer[0, 8].character);
        Assert.Equal('J', buffer[0, 9].character);
    }

    [Fact]
    public void ScrollUpInRegion_MultipleLines()
    {
        for (int y = 0; y < 10; y++)
        {
            buffer[0, y] = new Cell((char)('A' + y));
        }

        buffer.ScrollUpInRegion(2, 2, 5);

        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[0, 1].character);
        Assert.Equal('E', buffer[0, 2].character); // was row 4
        Assert.Equal('F', buffer[0, 3].character); // was row 5
        Assert.Equal(' ', buffer[0, 4].character); // cleared
        Assert.Equal(' ', buffer[0, 5].character); // cleared
        Assert.Equal('G', buffer[0, 6].character);
    }

    [Fact]
    public void ScrollDownInRegion_ExceedingRegionHeight_ClearsRegion()
    {
        for (int y = 0; y < 10; y++)
        {
            buffer[0, y] = new Cell((char)('A' + y));
        }

        buffer.ScrollDownInRegion(10, 3, 5);

        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal(' ', buffer[0, 3].character);
        Assert.Equal(' ', buffer[0, 4].character);
        Assert.Equal(' ', buffer[0, 5].character);
        Assert.Equal('G', buffer[0, 6].character);
    }
}
