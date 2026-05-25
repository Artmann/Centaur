using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// Prefixed CSI sequences ('&lt;' '=' '&gt;' '?') must not fall through to the ANSI
/// cursor/SGR handlers. Modern TUIs (e.g. Claude Code) emit the Kitty keyboard
/// protocol — CSI &gt; Pu u (push), CSI &lt; Pu u (pop), CSI = … u (set) — and
/// XTMODKEYS (CSI &gt; Pm m). With the private prefix now parsed, these reached
/// 'u' (RestoreCursor) / 'm' (SGR) / 's' (SaveCursor) and corrupted the screen,
/// interleaving Claude's redraw with prior shell output. The parser must ignore
/// the prefixed forms while leaving the unprefixed ANSI commands intact.
/// </summary>
public class VtParserPrefixedCsiTests
{
    readonly ScreenBuffer buffer;
    readonly VtParser parser;
    readonly TerminalTheme theme;

    public VtParserPrefixedCsiTests()
    {
        theme = CatppuccinThemes.Macchiato;
        buffer = new ScreenBuffer(80, 24, theme);
        parser = new VtParser(buffer, theme);
    }

    [Fact]
    public void KittyKeyboardPush_DoesNotMoveCursor()
    {
        parser.Send("hello");
        var x = buffer.cursorX;
        var y = buffer.cursorY;

        parser.Send("\x1b[>1u");

        Assert.Equal(x, buffer.cursorX);
        Assert.Equal(y, buffer.cursorY);
    }

    [Fact]
    public void KittyKeyboardPop_DoesNotMoveCursor()
    {
        parser.Send("hello");
        var x = buffer.cursorX;
        var y = buffer.cursorY;

        parser.Send("\x1b[<1u");

        Assert.Equal(x, buffer.cursorX);
        Assert.Equal(y, buffer.cursorY);
    }

    [Fact]
    public void KittyKeyboardSet_DoesNotMoveCursor()
    {
        parser.Send("hello");
        var x = buffer.cursorX;
        var y = buffer.cursorY;

        parser.Send("\x1b[=1;1u");

        Assert.Equal(x, buffer.cursorX);
        Assert.Equal(y, buffer.cursorY);
    }

    [Fact]
    public void RestoreCursor_Unprefixed_StillRestores()
    {
        // Move to (5,3), save, move away, then restore via ANSI RCP.
        parser.Send("\x1b[4;6H"); // row 4, col 6 -> (x=5, y=3)
        parser.Send("\x1b[s"); // SCP - save
        parser.Send("\x1b[20;40H"); // move elsewhere

        parser.Send("\x1b[u"); // RCP - restore

        Assert.Equal(5, buffer.cursorX);
        Assert.Equal(3, buffer.cursorY);
    }

    [Fact]
    public void XtModKeys_DoesNotApplyUnderlineOrFaint()
    {
        parser.Send("\x1b[>4;2mX");

        Assert.Equal(UnderlineStyle.None, buffer[0, 0].underline);
        Assert.False(buffer[0, 0].faint);
    }

    [Fact]
    public void SaveCursor_Prefixed_DoesNotOverwriteSaveSlot()
    {
        // Save position A.
        parser.Send("\x1b[4;6H"); // (x=5, y=3)
        parser.Send("\x1b[s");

        // Move to B and issue a *prefixed* 's' — must not save B.
        parser.Send("\x1b[20;40H");
        parser.Send("\x1b[>s");

        // Restore should return to A, not B.
        parser.Send("\x1b[u");

        Assert.Equal(5, buffer.cursorX);
        Assert.Equal(3, buffer.cursorY);
    }
}
