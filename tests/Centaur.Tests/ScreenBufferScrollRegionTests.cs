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
        Assert.Equal(0, buffer.Region.Top);
        Assert.Equal(9, buffer.Region.Bottom);
    }

    [Fact]
    public void SetScrollRegion_SetsTopAndBottom()
    {
        buffer.Region.Set(2, 7);

        Assert.Equal(2, buffer.Region.Top);
        Assert.Equal(7, buffer.Region.Bottom);
    }

    [Fact]
    public void SetScrollRegion_ClampsToValidRange()
    {
        buffer.Region.Set(-5, 100);

        Assert.Equal(0, buffer.Region.Top);
        Assert.Equal(9, buffer.Region.Bottom);
    }

    [Fact]
    public void SetScrollRegion_InvalidRange_ResetsToFullScreen()
    {
        buffer.Region.Set(5, 2);

        Assert.Equal(0, buffer.Region.Top);
        Assert.Equal(9, buffer.Region.Bottom);
    }

    [Fact]
    public void ScrollUpInRegion_OnlyAffectsRegionRows()
    {
        FillColumn();

        // Scroll within region 3..6 (rows 3,4,5,6); row 6 is left cleared.
        buffer.Region.ScrollUpIn(1, 3, 6);

        AssertColumn("ABCEFG HIJ");
    }

    [Fact]
    public void ScrollDownInRegion_OnlyAffectsRegionRows()
    {
        FillColumn();

        // Region 3..6 shifts down; row 3 is left cleared.
        buffer.Region.ScrollDownIn(1, 3, 6);

        AssertColumn("ABC DEFHIJ");
    }

    [Fact]
    public void ScrollUpInRegion_MultipleLines()
    {
        FillColumn();

        buffer.Region.ScrollUpIn(2, 2, 5);

        AssertColumn("ABEF  GHIJ");
    }

    [Fact]
    public void ScrollDownInRegion_ExceedingRegionHeight_ClearsRegion()
    {
        FillColumn();

        buffer.Region.ScrollDownIn(10, 3, 5);

        AssertColumn("ABC   GHIJ");
    }

    /// <summary>Writes A..J down column 0 so every row is individually identifiable.</summary>
    void FillColumn()
    {
        for (int y = 0; y < 10; y++)
        {
            buffer[0, y] = new Cell((char)('A' + y));
        }
    }

    /// <summary>Asserts the whole of column 0 at once, one character per row.</summary>
    void AssertColumn(string expected)
    {
        for (int y = 0; y < expected.Length; y++)
        {
            Assert.Equal(expected[y], buffer[0, y].character);
        }
    }
}
