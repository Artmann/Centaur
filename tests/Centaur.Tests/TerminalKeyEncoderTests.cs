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

    // DECCKM (mode 1): a program that turns application cursor keys on expects \x1b O A, not
    // \x1b [ A. Sending the wrong one is why arrow keys misbehave inside some full-screen apps.
    [Fact]
    public void Arrows_InNormalMode_UseTheCsiForm()
    {
        Assert.Equal("\x1b[A"u8.ToArray(), TerminalKeyEncoder.Encode(Key.Up, KeyModifiers.None));
        Assert.Equal("\x1b[B"u8.ToArray(), TerminalKeyEncoder.Encode(Key.Down, KeyModifiers.None));
    }

    [Fact]
    public void Arrows_InApplicationMode_UseTheSs3Form()
    {
        Assert.Equal(
            "\x1bOA"u8.ToArray(),
            TerminalKeyEncoder.Encode(Key.Up, KeyModifiers.None, applicationCursorKeys: true)
        );
        Assert.Equal(
            "\x1bOC"u8.ToArray(),
            TerminalKeyEncoder.Encode(Key.Right, KeyModifiers.None, applicationCursorKeys: true)
        );
    }

    // Home and End move with the arrows; the tilde keys and the function keys do not.
    [Fact]
    public void HomeAndEnd_FollowTheSameMode()
    {
        Assert.Equal(
            "\x1bOH"u8.ToArray(),
            TerminalKeyEncoder.Encode(Key.Home, KeyModifiers.None, applicationCursorKeys: true)
        );
        Assert.Equal("\x1b[F"u8.ToArray(), TerminalKeyEncoder.Encode(Key.End, KeyModifiers.None));
    }

    [Fact]
    public void ApplicationMode_LeavesOtherKeysAlone()
    {
        Assert.Equal(
            "\x1b[5~"u8.ToArray(),
            TerminalKeyEncoder.Encode(Key.PageUp, KeyModifiers.None, applicationCursorKeys: true)
        );
    }

    [Fact]
    public void UnmappedKey_ReturnsNull()
    {
        var bytes = TerminalKeyEncoder.Encode(Key.A, KeyModifiers.None);

        Assert.Null(bytes);
    }
}
