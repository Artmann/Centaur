using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Centaur.Core.Pty;
using Centaur.Core.Terminal;
using Centaur.Pty.Windows;

namespace Centaur.Tests;

/// <summary>
/// Headless end-to-end harness: spawns a real shell over ConPTY and wires its output
/// through the VtParser into a ScreenBuffer, exactly like <c>TerminalControl</c> does
/// (see TerminalControl.cs constructor + ReadLoopAsync), but with no Avalonia/UI thread.
///
/// Tests drive it by sending real commands and waiting for output to appear in the grid,
/// then read <see cref="ActiveBuffer"/> (or its text) to assert on the parsed screen state
/// and to feed the renderer. All buffer access is serialized under <see cref="bufferLock"/>
/// because the PTY read loop mutates the buffer on a background thread.
/// </summary>
sealed class TerminalSession : IAsyncDisposable
{
    readonly object bufferLock = new();
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly CancellationTokenSource readCts = new();
    readonly VtParser parser;
    readonly ScreenBuffer buffer;
    readonly ConPtyConnection pty;
    readonly Task readTask;

    TerminalSession(ConPtyConnection pty, VtParser parser, ScreenBuffer buffer)
    {
        this.pty = pty;
        this.parser = parser;
        this.buffer = buffer;
        readTask = Task.Run(() => ReadLoopAsync(readCts.Token));
    }

    public static async Task<TerminalSession> StartAsync(
        PtyOptions options,
        TerminalTheme? theme = null
    )
    {
        theme ??= CatppuccinThemes.Macchiato;
        var buffer = new ScreenBuffer(options.columns, options.rows, theme);
        var parser = new VtParser(buffer, theme);
        var pty = await SpawnAsync(options);

        var session = new TerminalSession(pty, parser, buffer);

        // Answer capability probes (Device Attributes, DECRQM, OSC color/clipboard) so the
        // shell doesn't stall on startup timeouts — mirrors TerminalControl.RespondToPty.
        parser.Respond += session.RespondToPty;

        return session;
    }

    /// <summary>The live parsed grid. Read it inside <see cref="WithBuffer"/> to stay race-free.</summary>
    public ScreenBuffer ActiveBuffer => parser.ActiveBuffer;

    /// <summary>Run <paramref name="read"/> against the live buffer while holding the parse lock.</summary>
    public T WithBuffer<T>(Func<ScreenBuffer, T> read)
    {
        lock (bufferLock)
        {
            return read(parser.ActiveBuffer);
        }
    }

    /// <summary>Type a line into the shell (UTF-8) followed by Enter (CR).</summary>
    public Task SendLineAsync(string text) => SendRawAsync(Encoding.UTF8.GetBytes(text + "\r"));

    /// <summary>Write raw bytes to the shell's stdin. Lets a test embed control bytes such as
    /// a literal ESC (0x1B) so the shell emits a deterministic escape sequence.</summary>
    public async Task SendRawAsync(byte[] bytes)
    {
        await writeLock.WaitAsync();
        try
        {
            await pty.Input.WriteAsync(bytes);
            await pty.Input.FlushAsync();
        }
        finally
        {
            writeLock.Release();
        }
    }

    /// <summary>Text of a single row with trailing blanks trimmed.</summary>
    public string GetLineText(int y) => WithBuffer(b => RowText(b, y));

    /// <summary>The whole visible grid as newline-joined rows, trailing blank rows trimmed.</summary>
    public string GetScreenText() =>
        WithBuffer(b =>
        {
            var sb = new StringBuilder();
            for (var y = 0; y < b.rows; y++)
            {
                sb.Append(RowText(b, y));
                if (y < b.rows - 1)
                {
                    sb.Append('\n');
                }
            }
            return sb.ToString().TrimEnd('\n');
        });

    /// <summary>Poll the grid until <paramref name="substring"/> appears, or the timeout elapses.
    /// Absorbs PTY timing and shell-startup nondeterminism — gate every assertion behind this.</summary>
    public async Task<bool> WaitForTextAsync(string substring, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            if (GetScreenText().Contains(substring, StringComparison.Ordinal))
            {
                return true;
            }
            await Task.Delay(20);
        }
        return GetScreenText().Contains(substring, StringComparison.Ordinal);
    }

    static string RowText(ScreenBuffer b, int y)
    {
        var sb = new StringBuilder(b.columns);
        var row = b.GetRow(y);
        for (var x = 0; x < row.Length; x++)
        {
            sb.Append(row[x].character);
        }
        return sb.ToString().TrimEnd();
    }

    void RespondToPty(byte[] data) => _ = SendRawAsync(data);

    // ConPTY connects a spawned child to the pseudoconsole's CONOUT$ only when the child does
    // not inherit a usable stdout from us (ConPtyConnection calls CreateProcess with
    // bInheritHandles: true). The real GUI app has null std handles, so that works. But the
    // `dotnet test` host has its std handles redirected to capture pipes, which the child would
    // inherit instead — its output would then go to the test runner, not our PTY, and we'd only
    // ever see conhost's init bytes. We reproduce the GUI app's environment by nulling our std
    // handles across the spawn, so the inherited child falls through to the pseudoconsole.
    // SetStdHandle is process-global (and CreateProcess runs on a thread pool thread, where the
    // handles are still in effect), so we serialize spawns and keep the window as small as possible.
    const int stdInputHandle = -10;
    const int stdOutputHandle = -11;
    const int stdErrorHandle = -12;

    static readonly SemaphoreSlim spawnGate = new(1, 1);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    static async Task<ConPtyConnection> SpawnAsync(PtyOptions options)
    {
        await spawnGate.WaitAsync();
        var savedOut = GetStdHandle(stdOutputHandle);
        var savedErr = GetStdHandle(stdErrorHandle);
        var savedIn = GetStdHandle(stdInputHandle);
        try
        {
            SetStdHandle(stdOutputHandle, IntPtr.Zero);
            SetStdHandle(stdErrorHandle, IntPtr.Zero);
            SetStdHandle(stdInputHandle, IntPtr.Zero);
            return await ConPtyConnection.CreateAsync(options);
        }
        finally
        {
            SetStdHandle(stdOutputHandle, savedOut);
            SetStdHandle(stdErrorHandle, savedErr);
            SetStdHandle(stdInputHandle, savedIn);
            spawnGate.Release();
        }
    }

    async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await pty.Output.ReadAsync(ct);
                var ptyBuffer = result.Buffer;

                lock (bufferLock)
                {
                    foreach (var segment in ptyBuffer)
                    {
                        parser.Process(segment.Span);
                    }
                }

                pty.Output.AdvanceTo(ptyBuffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose.
        }
        catch (Exception)
        {
            // Shell exited or pipe closed — nothing to assert here, tests gate on output.
        }
    }

    public async ValueTask DisposeAsync()
    {
        await readCts.CancelAsync();
        try
        {
            await readTask;
        }
        catch
        {
            // Loop already swallows its own exceptions; ignore.
        }
        await pty.DisposeAsync();
        readCts.Dispose();
        writeLock.Dispose();
    }
}
