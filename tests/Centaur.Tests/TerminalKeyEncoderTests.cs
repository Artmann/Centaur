using Avalonia.Input;
using Centaur.App;
using Xunit;

namespace Centaur.Tests;

public class TerminalKeyEncoderTests
{
    [Fact]
    public void Tab_WithNoModifiers_SendsHorizontalTab()
    {
        var bytes = TerminalKeyEncoder.Encode(Key.Tab, KeyModifiers.None);

        Assert.Equal("\t"u8.ToArray(), bytes);
    }

    [Fact]
    public void Tab_WithShift_SendsBacktabEscapeSequence()
    {
        var bytes = TerminalKeyEncoder.Encode(Key.Tab, KeyModifiers.Shift);

        Assert.Equal("\x1b[Z"u8.ToArray(), bytes);
    }

    [Fact]
    public void Enter_WithNoModifiers_SendsCarriageReturn()
    {
        var bytes = TerminalKeyEncoder.Encode(Key.Enter, KeyModifiers.None);

        Assert.Equal("\r"u8.ToArray(), bytes);
    }

    [Fact]
    public void UnmappedKey_ReturnsNull()
    {
        var bytes = TerminalKeyEncoder.Encode(Key.A, KeyModifiers.None);

        Assert.Null(bytes);
    }
}
