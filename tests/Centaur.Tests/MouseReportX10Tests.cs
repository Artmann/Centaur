using System.Linq;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// The legacy X10 mouse report, used whenever the program enables tracking without also
/// enabling SGR mode (1006): ESC [ M Cb Cx Cy, where each of the three is its value plus 32
/// so it lands in the printable range.
///
/// That offset is also the format's limit - a coordinate can only reach 223 before the byte
/// would leave Latin-1 - so wide windows clamp rather than wrap onto a different cell.
/// </summary>
public class MouseReportX10Tests
{
    // Written as raw values rather than escapes: the report is bytes, not text, and the
    // interesting ones are unprintable.
    static string Report(params int[] values) =>
        "\x1b[M" + new string(values.Select(v => (char)v).ToArray());

    [Fact]
    public void PressLeft_AtOrigin()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.Left,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );

        Assert.Equal(Report(32, 33, 33), report);
    }

    // X10 has no per-button release: every release is the "no button" code 3.
    [Fact]
    public void Release_UsesTheNoButtonCode()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.Left,
            0,
            0,
            MouseAction.Release,
            MouseModifiers.None
        );

        Assert.Equal(Report(35, 33, 33), report);
    }

    [Fact]
    public void RightButton_Code2()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.Right,
            9,
            4,
            MouseAction.Press,
            MouseModifiers.None
        );

        Assert.Equal(Report(34, 42, 37), report);
    }

    [Fact]
    public void ScrollUp_Code64()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.ScrollUp,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );

        Assert.Equal(Report(96, 33, 33), report);
    }

    [Fact]
    public void Motion_AddsThirtyTwo()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.Left,
            5,
            6,
            MouseAction.Motion,
            MouseModifiers.None
        );

        Assert.Equal(Report(64, 38, 39), report);
    }

    [Fact]
    public void Modifiers_AddBits()
    {
        // left(0) + shift(4) + ctrl(16) = 20.
        var report = MouseReport.EncodeX10(
            MouseButton.Left,
            0,
            0,
            MouseAction.Press,
            MouseModifiers.Shift | MouseModifiers.Ctrl
        );

        Assert.Equal(Report(52, 33, 33), report);
    }

    [Fact]
    public void CoordinatesBeyondTheFormat_ClampToTheLastEncodableCell()
    {
        var report = MouseReport.EncodeX10(
            MouseButton.Left,
            400,
            300,
            MouseAction.Press,
            MouseModifiers.None
        );

        Assert.Equal(Report(32, 255, 255), report);
    }

    // Column 222 is the last one that fits (223 one-based, 255 with the offset), so it and
    // the one before it must still differ - the clamp starts above it, not at it.
    [Fact]
    public void TheLastEncodableCells_AreNotClamped()
    {
        var last = MouseReport.EncodeX10(
            MouseButton.Left,
            222,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );
        var previous = MouseReport.EncodeX10(
            MouseButton.Left,
            221,
            0,
            MouseAction.Press,
            MouseModifiers.None
        );

        Assert.Equal(Report(32, 255, 33), last);
        Assert.Equal(Report(32, 254, 33), previous);
    }
}
