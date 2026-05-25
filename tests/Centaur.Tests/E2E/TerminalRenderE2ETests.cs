using System.Diagnostics;
using Centaur.Core.Pty;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

/// <summary>
/// End-to-end tests: spawn a real shell over ConPTY, run actual commands, then assert the
/// output is rendered correctly by sampling pixels from the real <see cref="TerminalRenderer"/>.
///
/// These drive the whole pipeline — ConPtyConnection -> VtParser -> ScreenBuffer ->
/// TerminalRenderer -> bitmap. They are Windows-only (so is the PTY) and gate every assertion
/// behind <see cref="TerminalSession.WaitForTextAsync"/> to stay robust against shell timing.
/// ESC is embedded in raw input so colored output doesn't depend on shell color support.
/// </summary>
public class TerminalRenderE2ETests
{
    static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(15);
    const string marker = "centaur_e2e_marker";

    static readonly TerminalTheme theme = CatppuccinThemes.Macchiato;

    static PtyOptions CmdOptions() => new(executable: "cmd.exe", columns: 80, rows: 24);

    [Fact]
    public async Task PlainText_EchoedCommand_RendersGlyphPixels()
    {
        await using var session = await TerminalSession.StartAsync(CmdOptions(), theme);
        await session.SendLineAsync($"echo {marker}");

        Assert.True(
            await session.WaitForTextAsync(marker, WaitTimeout),
            "shell never echoed the marker"
        );
        var row = await WaitForRowAsync(session, line => line == marker, WaitTimeout);
        Assert.True(row >= 0, "marker never appeared on its own output line");

        using var renderer = new TerminalRenderer(theme);
        var snapshot = session.WithBuffer(b => b.Snapshot());
        using var bitmap = RenderProbe.RenderToBitmap(snapshot, renderer);

        var background = RenderProbe.ToColor(theme.Background);

        // Every glyph in the rendered marker must have drawn ink.
        for (var col = 0; col < marker.Length; col++)
        {
            var ink = RenderProbe.ForegroundPixelCount(bitmap, renderer, col, row, background);
            Assert.True(ink > 0, $"no glyph pixels rendered for '{marker[col]}' at column {col}");
        }

        // A blank cell on the same row stays the theme background.
        var blank = RenderProbe.CellCenterPixel(bitmap, renderer, snapshot.columns - 1, row);
        Assert.True(
            RenderProbe.ColorsClose(blank, background),
            $"expected background {background} in a blank cell but got {blank}"
        );
    }

    [Fact]
    public async Task EmptyCell_RendersThemeBackground()
    {
        await using var session = await TerminalSession.StartAsync(CmdOptions(), theme);
        await session.SendLineAsync($"echo {marker}");
        Assert.True(
            await session.WaitForTextAsync(marker, WaitTimeout),
            "shell produced no output"
        );

        using var renderer = new TerminalRenderer(theme);
        var (col, snapRow, snapshot) = session.WithBuffer(b =>
        {
            var (c, r) = FindBlankCell(b);
            return (c, r, b.Snapshot());
        });
        Assert.True(snapRow >= 0, "could not find a blank cell to sample");

        using var bitmap = RenderProbe.RenderToBitmap(snapshot, renderer, cursorVisible: false);
        var pixel = RenderProbe.CellCenterPixel(bitmap, renderer, col, snapRow);

        Assert.True(
            RenderProbe.ColorsClose(pixel, RenderProbe.ToColor(theme.Background)),
            $"blank cell ({col},{snapRow}) rendered {pixel}, expected theme background"
        );
    }

    [Fact]
    public async Task ColoredOutput_RendersRedForeground()
    {
        await using var session = await TerminalSession.StartAsync(CmdOptions(), theme);

        // Emit a real SGR sequence from the shell's *output* (ESC[31m RED ESC[0m). Raw ESC bytes
        // sent as *input* are swallowed by conhost's input processing, so we make a child process
        // write the escape instead: powershell builds it from [char]27 and writes it to stdout.
        // conhost colors the cell, VtParser parses the SGR back out — exercising the full path.
        await session.SendLineAsync(
            "powershell -NoProfile -Command \"$e=[string][char]27;Write-Host ($e+'[31mRED'+$e+'[0m')\""
        );

        Assert.True(
            await session.WaitForTextAsync("RED", WaitTimeout),
            "colored output never appeared"
        );
        var row = await WaitForRowAsync(session, line => line == "RED", WaitTimeout);
        Assert.True(row >= 0, "'RED' never appeared on its own output line");

        using var renderer = new TerminalRenderer(theme);
        var snapshot = session.WithBuffer(b => b.Snapshot());
        using var bitmap = RenderProbe.RenderToBitmap(snapshot, renderer);

        var background = RenderProbe.ToColor(theme.Background);
        var ink = RenderProbe.DominantForegroundColor(bitmap, renderer, 0, row, background);

        // The rendered ink must be red-dominant — clearly redder than it is green or blue, and
        // not the bluish default foreground. (Exact match is avoided due to antialiasing.)
        Assert.True(
            ink.Red > ink.Green + 30 && ink.Red > ink.Blue + 30 && ink.Red > 120,
            $"expected a red glyph but rendered ink was {ink}"
        );
    }

    [Fact]
    public async Task Cursor_RendersCursorColoredBlock()
    {
        await using var session = await TerminalSession.StartAsync(CmdOptions(), theme);
        await session.SendLineAsync($"echo {marker}");
        Assert.True(
            await session.WaitForTextAsync(marker, WaitTimeout),
            "shell produced no prompt"
        );

        var snapshot = session.WithBuffer(b => b.Snapshot());
        Assert.InRange(snapshot.cursorX, 0, snapshot.columns - 1);
        Assert.InRange(snapshot.cursorY, 0, snapshot.rows - 1);

        using var renderer = new TerminalRenderer(theme);
        using var withCursor = RenderProbe.RenderToBitmap(snapshot, renderer, cursorVisible: true);
        using var withoutCursor = RenderProbe.RenderToBitmap(
            snapshot,
            renderer,
            cursorVisible: false
        );

        var cursorColor = RenderProbe.ToColor(theme.Cursor);
        var col = snapshot.cursorX;
        var crow = snapshot.cursorY;

        var visible = RenderProbe.CountPixelsClose(withCursor, renderer, col, crow, cursorColor);
        var hidden = RenderProbe.CountPixelsClose(withoutCursor, renderer, col, crow, cursorColor);

        Assert.True(visible > 0, "cursor cell rendered no cursor-colored pixels");
        Assert.True(
            visible > hidden,
            $"cursor added no ink: {visible} cursor-colored pixels with cursor vs {hidden} without"
        );
    }

    /// <summary>Poll the grid until some row satisfies <paramref name="predicate"/>; returns its
    /// index or -1. Used to wait for a command's output line (not the echoed input line).</summary>
    static async Task<int> WaitForRowAsync(
        TerminalSession session,
        Func<string, bool> predicate,
        TimeSpan timeout
    )
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            var row = session.WithBuffer(b => FindRow(b, predicate));
            if (row >= 0)
            {
                return row;
            }
            await Task.Delay(20);
        }
        return session.WithBuffer(b => FindRow(b, predicate));
    }

    static int FindRow(ScreenBuffer b, Func<string, bool> predicate)
    {
        for (var y = 0; y < b.rows; y++)
        {
            if (predicate(RowText(b, y)))
            {
                return y;
            }
        }
        return -1;
    }

    static (int col, int row) FindBlankCell(ScreenBuffer b)
    {
        // Search from the bottom — the lower rows of a freshly started shell are empty.
        for (var y = b.rows - 1; y >= 0; y--)
        {
            for (var x = b.columns - 1; x >= 0; x--)
            {
                if (b[x, y].character == ' ' && !(x == b.cursorX && y == b.cursorY))
                {
                    return (x, y);
                }
            }
        }
        return (-1, -1);
    }

    static string RowText(ScreenBuffer b, int y)
    {
        var row = b.GetRow(y);
        var chars = new char[row.Length];
        for (var x = 0; x < row.Length; x++)
        {
            chars[x] = row[x].character;
        }
        return new string(chars).TrimEnd();
    }
}
