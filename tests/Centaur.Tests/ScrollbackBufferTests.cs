using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class ScrollbackBufferTests
{
    [Fact]
    public void NewBuffer_HasZeroCount()
    {
        var buffer = new ScrollbackBuffer(100);

        Assert.Equal(0, buffer.Count);
        Assert.Equal(100, buffer.Capacity);
    }

    [Fact]
    public void PushLine_IncreasesCount()
    {
        var buffer = new ScrollbackBuffer(100);
        var row = new Cell[] { new('A'), new('B'), new('C') };

        buffer.PushLine(row);

        Assert.Equal(1, buffer.Count);
    }

    [Fact]
    public void PushLine_StoresLineContent()
    {
        var buffer = new ScrollbackBuffer(100);
        var row = new Cell[] { new('X'), new('Y'), new('Z') };

        buffer.PushLine(row);

        var stored = buffer.GetLine(0);
        Assert.Equal(3, stored.Length);
        Assert.Equal('X', stored[0].character);
        Assert.Equal('Y', stored[1].character);
        Assert.Equal('Z', stored[2].character);
    }

    [Fact]
    public void PushLine_CopiesData_NotReference()
    {
        var buffer = new ScrollbackBuffer(100);
        var row = new Cell[] { new('A'), new('B') };

        buffer.PushLine(row);
        row[0] = new Cell('Z');

        var stored = buffer.GetLine(0);
        Assert.Equal('A', stored[0].character);
    }

    [Fact]
    public void GetLine_IndexZeroIsOldest()
    {
        var buffer = new ScrollbackBuffer(100);
        buffer.PushLine(new Cell[] { new('1') });
        buffer.PushLine(new Cell[] { new('2') });
        buffer.PushLine(new Cell[] { new('3') });

        Assert.Equal('1', buffer.GetLine(0)[0].character);
        Assert.Equal('2', buffer.GetLine(1)[0].character);
        Assert.Equal('3', buffer.GetLine(2)[0].character);
    }

    [Fact]
    public void PushLine_CircularOverwrite_OldestIsLost()
    {
        var buffer = new ScrollbackBuffer(3);
        buffer.PushLine(new Cell[] { new('A') });
        buffer.PushLine(new Cell[] { new('B') });
        buffer.PushLine(new Cell[] { new('C') });
        buffer.PushLine(new Cell[] { new('D') });

        Assert.Equal(3, buffer.Count);
        Assert.Equal('B', buffer.GetLine(0)[0].character);
        Assert.Equal('C', buffer.GetLine(1)[0].character);
        Assert.Equal('D', buffer.GetLine(2)[0].character);
    }

    [Fact]
    public void PushLine_CircularOverwrite_MultipleCycles()
    {
        var buffer = new ScrollbackBuffer(2);
        for (int i = 0; i < 10; i++)
        {
            buffer.PushLine(new Cell[] { new((char)('A' + i)) });
        }

        Assert.Equal(2, buffer.Count);
        Assert.Equal('I', buffer.GetLine(0)[0].character);
        Assert.Equal('J', buffer.GetLine(1)[0].character);
    }

    [Fact]
    public void Clear_ResetsCount()
    {
        var buffer = new ScrollbackBuffer(100);
        buffer.PushLine(new Cell[] { new('A') });
        buffer.PushLine(new Cell[] { new('B') });

        buffer.Clear();

        Assert.Equal(0, buffer.Count);
    }

    [Fact]
    public void PushLine_VaryingRowWidths()
    {
        var buffer = new ScrollbackBuffer(100);
        buffer.PushLine(new Cell[] { new('A'), new('B') });
        buffer.PushLine(new Cell[] { new('X'), new('Y'), new('Z'), new('W') });

        Assert.Equal(2, buffer.GetLine(0).Length);
        Assert.Equal(4, buffer.GetLine(1).Length);
    }
}
