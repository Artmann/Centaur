using Avalonia.Input;
using Centaur.App;
using Xunit;

namespace Centaur.Tests;

public class KeyShortcutTableTests
{
    [Fact]
    public void UnregisteredKey_IsNotHandled()
    {
        var table = new KeyShortcutTable().Add(Key.V, KeyModifiers.Control, () => { });

        Assert.False(table.TryHandle(Key.X, KeyModifiers.Control));
    }

    [Fact]
    public void MatchingKeyAndModifier_RunsTheHandler()
    {
        var ran = false;
        var table = new KeyShortcutTable().Add(Key.V, KeyModifiers.Control, () => ran = true);

        Assert.True(table.TryHandle(Key.V, KeyModifiers.Control));
        Assert.True(ran);
    }

    [Fact]
    public void RightKeyWithoutTheModifier_IsNotHandled()
    {
        var ran = false;
        var table = new KeyShortcutTable().Add(Key.V, KeyModifiers.Control, () => ran = true);

        Assert.False(table.TryHandle(Key.V, KeyModifiers.None));
        Assert.False(ran);
    }

    [Fact]
    public void ExtraModifiersStillMatch()
    {
        var table = new KeyShortcutTable().Add(Key.C, KeyModifiers.Control, () => { });

        Assert.True(table.TryHandle(Key.C, KeyModifiers.Control | KeyModifiers.Shift));
    }

    [Fact]
    public void AllRequiredModifiersMustBeHeld()
    {
        var table = new KeyShortcutTable().Add(
            Key.P,
            KeyModifiers.Control | KeyModifiers.Shift,
            () => { }
        );

        Assert.False(table.TryHandle(Key.P, KeyModifiers.Control));
        Assert.True(table.TryHandle(Key.P, KeyModifiers.Control | KeyModifiers.Shift));
    }

    [Fact]
    public void NoModifiers_DemandsABareKeyPress()
    {
        var table = new KeyShortcutTable().Add(Key.Tab, KeyModifiers.None, () => { });

        Assert.True(table.TryHandle(Key.Tab, KeyModifiers.None));
        Assert.False(table.TryHandle(Key.Tab, KeyModifiers.Shift));
    }

    [Fact]
    public void DecliningHandler_LetsTheKeyFallThrough()
    {
        var table = new KeyShortcutTable().Add(Key.C, KeyModifiers.Control, () => false);

        Assert.False(table.TryHandle(Key.C, KeyModifiers.Control));
    }

    [Fact]
    public void DecliningHandler_PassesTheKeyToTheNextMatchingEntry()
    {
        var second = false;
        var table = new KeyShortcutTable()
            .Add(Key.C, KeyModifiers.Control, () => false)
            .Add(
                Key.C,
                KeyModifiers.Control,
                () =>
                {
                    second = true;
                    return true;
                }
            );

        Assert.True(table.TryHandle(Key.C, KeyModifiers.Control));
        Assert.True(second);
    }

    [Fact]
    public void FirstMatchingEntryWins()
    {
        var order = new List<string>();
        var table = new KeyShortcutTable()
            .Add(Key.P, KeyModifiers.Control | KeyModifiers.Shift, () => order.Add("profiler"))
            .Add(Key.P, KeyModifiers.Control, () => order.Add("control byte"));

        table.TryHandle(Key.P, KeyModifiers.Control | KeyModifiers.Shift);

        Assert.Equal(["profiler"], order);
    }
}
