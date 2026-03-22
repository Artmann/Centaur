using System.Text;
using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class VtParserModesTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserModesTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    void Send(string text)
    {
        parser.Process(Encoding.ASCII.GetBytes(text));
    }

    // === Feature 1: DEC Private Mode Handling ===

    [Fact]
    public void CursorVisible_DefaultTrue()
    {
        Assert.True(parser.CursorVisible);
    }

    [Fact]
    public void HideCursor_SetsCursorVisibleFalse()
    {
        Send("\x1b[?25l");

        Assert.False(parser.CursorVisible);
    }

    [Fact]
    public void ShowCursor_SetsCursorVisibleTrue()
    {
        Send("\x1b[?25l");
        Send("\x1b[?25h");

        Assert.True(parser.CursorVisible);
    }

    [Fact]
    public void ApplicationCursorKeys_SetAndReset()
    {
        Assert.False(parser.ApplicationCursorKeys);

        Send("\x1b[?1h");
        Assert.True(parser.ApplicationCursorKeys);

        Send("\x1b[?1l");
        Assert.False(parser.ApplicationCursorKeys);
    }

    [Fact]
    public void BracketedPasteMode_SetAndReset()
    {
        Assert.False(parser.BracketedPasteMode);

        Send("\x1b[?2004h");
        Assert.True(parser.BracketedPasteMode);

        Send("\x1b[?2004l");
        Assert.False(parser.BracketedPasteMode);
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

        Assert.Equal(4, buffer.scrollTop);
        Assert.Equal(9, buffer.scrollBottom);
    }

    [Fact]
    public void ScrollRegion_LineFeedAtBottom_ScrollsWithinRegion()
    {
        // Set scroll region rows 2..5 (1-based: 3..6)
        Send("\x1b[3;6r");

        // Write identifiable content in each row
        for (int y = 0; y < 24; y++)
        {
            buffer[0, y] = new Cell((char)('A' + (y % 26)));
        }

        // Position cursor at bottom of scroll region (row 5, 0-based)
        buffer.cursorX = 0;
        buffer.cursorY = 5;

        // Send line feed - should scroll within region
        Send("\n");

        // Row 0,1 untouched (outside region)
        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[0, 1].character);

        // Region scrolled up: row 2 now has what was in row 3
        Assert.Equal('D', buffer[0, 2].character);
        Assert.Equal('E', buffer[0, 3].character);
        Assert.Equal('F', buffer[0, 4].character);
        // Row 5 (bottom of region) cleared
        Assert.Equal(' ', buffer[0, 5].character);

        // Rows after region untouched
        Assert.Equal('G', buffer[0, 6].character);
    }

    [Fact]
    public void ScrollRegion_ReverseIndexAtTop_ScrollsDownWithinRegion()
    {
        Send("\x1b[3;6r"); // region rows 2..5

        for (int y = 0; y < 24; y++)
        {
            buffer[0, y] = new Cell((char)('A' + (y % 26)));
        }

        // Position cursor at top of scroll region
        buffer.cursorX = 0;
        buffer.cursorY = 2;

        // ESC M = Reverse Index
        Send("\x1bM");

        // Rows before region untouched
        Assert.Equal('A', buffer[0, 0].character);
        Assert.Equal('B', buffer[0, 1].character);

        // Top of region cleared (new blank line inserted)
        Assert.Equal(' ', buffer[0, 2].character);
        // Region shifted down
        Assert.Equal('C', buffer[0, 3].character);
        Assert.Equal('D', buffer[0, 4].character);
        Assert.Equal('E', buffer[0, 5].character);

        // Rows after region untouched
        Assert.Equal('G', buffer[0, 6].character);
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
