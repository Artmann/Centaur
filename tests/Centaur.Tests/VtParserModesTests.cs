using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class VtParserModesTests : VtParserFixture
{
    /// <summary>Writes A, B, C... down column 0 so every row is identifiable after scrolling.</summary>
    void FillColumn()
    {
        for (int y = 0; y < 24; y++)
        {
            buffer[0, y] = new Cell((char)('A' + (y % 26)));
        }
    }

    /// <summary>Asserts the leading rows of column 0 at once, one character per row.</summary>
    void AssertColumn(string expected)
    {
        for (int y = 0; y < expected.Length; y++)
        {
            Assert.Equal(expected[y], buffer[0, y].character);
        }
    }

    // === Feature 1: DEC Private Mode Handling ===

    [Fact]
    public void CursorVisible_DefaultTrue()
    {
        Assert.True(parser.Modes.CursorVisible);
    }

    [Fact]
    public void HideCursor_SetsCursorVisibleFalse()
    {
        Send("\x1b[?25l");

        Assert.False(parser.Modes.CursorVisible);
    }

    [Fact]
    public void ShowCursor_SetsCursorVisibleTrue()
    {
        Send("\x1b[?25l");
        Send("\x1b[?25h");

        Assert.True(parser.Modes.CursorVisible);
    }

    [Fact]
    public void ApplicationCursorKeys_SetAndReset()
    {
        Assert.False(parser.Modes.ApplicationCursorKeys);

        Send("\x1b[?1h");
        Assert.True(parser.Modes.ApplicationCursorKeys);

        Send("\x1b[?1l");
        Assert.False(parser.Modes.ApplicationCursorKeys);
    }

    [Fact]
    public void BracketedPasteMode_SetAndReset()
    {
        Assert.False(parser.Modes.BracketedPasteMode);

        Send("\x1b[?2004h");
        Assert.True(parser.Modes.BracketedPasteMode);

        Send("\x1b[?2004l");
        Assert.False(parser.Modes.BracketedPasteMode);
    }

    // === Feature 2: Cursor Save/Restore ===

    [Fact]
    public void CursorSaveRestore_PreservesPosition()
    {
        buffer.cursorX = 10;
        buffer.cursorY = 5;
        Send("\x1b" + "7"); // Save

        buffer.cursorX = 0;
        buffer.cursorY = 0;
        Send("\x1b" + "8"); // Restore

        Assert.Equal(10, buffer.cursorX);
        Assert.Equal(5, buffer.cursorY);
    }

    [Fact]
    public void CursorSaveRestore_PreservesColors()
    {
        // Set red foreground (color index 1 = red)
        Send("\x1b[31m");
        Send("\x1b" + "7"); // Save

        // Change to blue (color index 4)
        Send("\x1b[34m");

        Send("\x1b" + "8"); // Restore

        // Write a character and check its color matches saved red
        Send("X");
        var cell = buffer[0, 0];
        Assert.Equal(theme.GetColor(1), cell.foreground); // red
    }

    // === Feature 3: Scroll Regions ===

    [Fact]
    public void DecstbmResetsCursorToHome()
    {
        buffer.cursorX = 10;
        buffer.cursorY = 5;

        Send("\x1b[1;10r");

        Assert.Equal(0, buffer.cursorX);
        Assert.Equal(0, buffer.cursorY);
    }

    [Fact]
    public void DecstbmSetsScrollRegion()
    {
        Send("\x1b[5;10r"); // 1-based, so 0-based is 4..9

        Assert.Equal(4, buffer.Region.Top);
        Assert.Equal(9, buffer.Region.Bottom);
    }

    [Fact]
    public void ScrollRegion_LineFeedAtBottom_ScrollsWithinRegion()
    {
        // Set scroll region rows 2..5 (1-based: 3..6)
        Send("\x1b[3;6r");

        FillColumn();

        // Position cursor at bottom of scroll region (row 5, 0-based)
        buffer.cursorX = 0;
        buffer.cursorY = 5;

        // Send line feed - should scroll within region
        Send("\n");

        // Rows outside the region untouched; region scrolled up with row 5 cleared.
        AssertColumn("ABDEF G");
    }

    [Fact]
    public void ScrollRegion_ReverseIndexAtTop_ScrollsDownWithinRegion()
    {
        Send("\x1b[3;6r"); // region rows 2..5

        FillColumn();

        // Position cursor at top of scroll region
        buffer.cursorX = 0;
        buffer.cursorY = 2;

        // ESC M = Reverse Index
        Send("\x1bM");

        // Rows outside the region untouched; region shifted down with row 2 cleared.
        AssertColumn("AB CDEG");
    }

    // === Feature 4: Alternate Screen Buffer ===

    [Fact]
    public void AlternateScreen_SwitchToAlt_ClearsScreen()
    {
        Send("Hello");

        Send("\x1b[?1049h"); // Switch to alt

        Assert.True(parser.IsAlternateScreen);
        // Alt screen should be clean
        Assert.Equal(' ', parser.ActiveBuffer[0, 0].character);
    }

    [Fact]
    public void AlternateScreen_SwitchBack_RestoresContent()
    {
        Send("Hello");

        Send("\x1b[?1049h"); // Switch to alt
        Send("Alt text");

        Send("\x1b[?1049l"); // Switch back

        Assert.False(parser.IsAlternateScreen);
        // Original content should be preserved
        Assert.Equal('H', buffer[0, 0].character);
        Assert.Equal('e', buffer[1, 0].character);
    }

    [Fact]
    public void AlternateScreen_SavesAndRestoresCursor()
    {
        buffer.cursorX = 15;
        buffer.cursorY = 10;

        Send("\x1b[?1049h"); // Switch to alt (saves cursor)

        // Move cursor on alt screen
        buffer.cursorX = 0;
        buffer.cursorY = 0;

        Send("\x1b[?1049l"); // Switch back (restores cursor)

        Assert.Equal(15, buffer.cursorX);
        Assert.Equal(10, buffer.cursorY);
    }

    [Fact]
    public void AlternateScreen_AppSavesCursorOnAlt_DoesNotCorruptMainRestore()
    {
        // Shell prompt sits at (15, 10) on the main screen before a full-screen app runs.
        buffer.cursorX = 15;
        buffer.cursorY = 10;

        Send("\x1b[?1049h"); // Enter alt screen — saves the main-screen cursor

        // Full-screen app (e.g. Claude Code) moves its cursor and does its OWN
        // save/restore (DECSC/DECRC) while drawing on the alt screen.
        Send("\x1b[3;5H"); // move to (col 4, row 2) on alt
        Send("\x1b" + "7"); // DECSC on alt screen
        Send("\x1b[20;40H"); // move elsewhere on alt
        Send("\x1b" + "8"); // DECRC on alt screen

        Send("\x1b[?1049l"); // Exit alt screen — must restore the main-screen cursor

        // The app's alt-screen save/restore must not leak into the main screen.
        Assert.Equal(15, buffer.cursorX);
        Assert.Equal(10, buffer.cursorY);
    }

    [Fact]
    public void AlternateScreen_ActiveBufferReturnsCurrentBuffer()
    {
        var mainBuf = parser.ActiveBuffer;
        Assert.Same(buffer, mainBuf);

        Send("\x1b[?1049h");
        var altBuf = parser.ActiveBuffer;
        Assert.NotSame(buffer, altBuf);

        Send("\x1b[?1049l");
        Assert.Same(buffer, parser.ActiveBuffer);
    }
}
