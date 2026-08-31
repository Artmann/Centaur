using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// DECSC/DECRC (ESC 7 / ESC 8) and SCP/RCP (CSI s / CSI u), and the register the
/// alternate-screen switch (DEC 1049) uses on the way in and out.
///
/// The saved cursor is not just a position: VT100 and xterm both save the graphic
/// rendition with it, so a restore has to put the whole pen back. Saving only the
/// colours leaves the style flags at whatever the pen happened to reach, and a program
/// that turns inverse on between the save and the restore gets it stuck on afterwards -
/// every space it prints then paints as a solid block of its foreground colour.
/// </summary>
public class VtParserSaveRestoreTests : VtParserFixture
{
    // Built from a char literal rather than written inline: \x is a variable-length
    // escape in C#, so "\x1b7" would be the single character U+01B7 rather than ESC
    // followed by a 7.
    const char esc = '\x1b';
    static readonly string decsc = $"{esc}7";
    static readonly string decrc = $"{esc}8";

    // The artifact, reduced: green foreground saved without inverse, inverse turned on
    // in between, then restored. The spaces printed afterwards must not be inverse.
    [Fact]
    public void Decrc_ClearsInverseTurnedOnAfterTheSave()
    {
        Send($"\x1b[32m{decsc}\x1b[7m{decrc} ");

        Assert.False(buffer[0, 0].inverse);
    }

    [Fact]
    public void Decrc_RestoresInverseThatWasOnAtTheSave()
    {
        Send($"\x1b[7m{decsc}\x1b[27m{decrc}X");

        Assert.True(buffer[0, 0].inverse);
    }

    [Fact]
    public void Decrc_RestoresEveryStyleFlag()
    {
        // Every style SGR the pen knows, plus an underline colour and a 256-colour pair.
        Send("\x1b[1;2;3;5;7;8;9;53m\x1b[4:3m\x1b[58;5;1m\x1b[38;5;2m\x1b[48;5;4mX");
        var styled = buffer[0, 0];

        Send($"{decsc}\x1b[0m{decrc}Y");

        Assert.Equal(styled with { character = 'Y' }, buffer[1, 0]);
    }

    [Fact]
    public void Decrc_RestoresCursorPosition()
    {
        Send($"\x1b[5;10H{decsc}\x1b[20;40H{decrc}X");

        Assert.Equal('X', buffer[9, 4].character);
    }

    // CSI s / CSI u is the ANSI.SYS spelling of the same register.
    [Fact]
    public void Rcp_RestoresStyles()
    {
        Send("\x1b[32m\x1b[s\x1b[7m\x1b[u ");

        Assert.False(buffer[0, 0].inverse);
    }

    // With nothing saved, a restore is defined to home the cursor and reset the rendition.
    // The uninitialised register used to hand back a transparent black pen instead.
    [Fact]
    public void Decrc_BeforeAnyDecsc_RestoresThemeDefaults()
    {
        Send($"\x1b[3;7H\x1b[31;7mX{decrc}Y");

        var restored = buffer[0, 0];
        Assert.Equal('Y', restored.character);
        Assert.Equal(theme.Foreground, restored.foreground);
        Assert.Equal(theme.Background, restored.background);
        Assert.False(restored.inverse);
    }

    // OSC 8 owns the hyperlink, not SGR - the same reason Reset() carries it through.
    [Fact]
    public void Decrc_KeepsTheCurrentHyperlink()
    {
        Send($"{decsc}\x1b]8;;https://example.com\x1b\\{decrc}X");

        Assert.Equal("https://example.com", buffer[0, 0].hyperlink);
    }

    // 1049h saves the main screen's pen and 1049l puts it back, through the same register.
    [Fact]
    public void AlternateScreen_RoundTripsTheMainPenStyles()
    {
        Send("\x1b[32;7m");
        Send("\x1b[?1049h\x1b[0m\x1b[31mX\x1b[?1049l");
        Send("Y");

        var restored = buffer[0, 0];
        Assert.True(restored.inverse);
        Assert.Equal(theme.GetColor(2), restored.foreground);
    }

    // Each screen has its own register, so a save on the alternate screen must not be
    // what 1049l hands back to the main screen.
    [Fact]
    public void Decsc_OnAlternateScreen_LeavesTheMainRegisterAlone()
    {
        Send("\x1b[32;7m\x1b[?1049h");
        Send($"\x1b[0m\x1b[34m{decsc}\x1b[1m{decrc}");
        Send("\x1b[?1049lY");

        var restored = buffer[0, 0];
        Assert.True(restored.inverse);
        Assert.Equal(theme.GetColor(2), restored.foreground);
        Assert.False(restored.bold);
    }
}
