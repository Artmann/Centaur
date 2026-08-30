using System.Runtime.InteropServices;
using Centaur.Core.Pty;
using Centaur.Pty.Windows;

namespace Centaur.Tests;

/// <summary>
/// Spawns a ConPTY child with this process's std handles temporarily nulled.
///
/// ConPTY connects a spawned child to the pseudoconsole's CONOUT$ only when the child does
/// not inherit a usable stdout from us (ConPtyConnection calls CreateProcess with
/// bInheritHandles: true). The real GUI app has null std handles, so that works. But the
/// `dotnet test` host has its std handles redirected to capture pipes, which the child would
/// inherit instead — its output would then go to the test runner, not our PTY, and we'd only
/// ever see conhost's init bytes. We reproduce the GUI app's environment by nulling our std
/// handles across the spawn, so the inherited child falls through to the pseudoconsole.
///
/// SetStdHandle is process-global (and CreateProcess runs on a thread pool thread, where the
/// handles are still in effect), so spawns are serialized and the window kept as small as possible.
/// </summary>
static class NullStdHandleSpawn
{
    const int stdInputHandle = -10;
    const int stdOutputHandle = -11;
    const int stdErrorHandle = -12;

    static readonly SemaphoreSlim spawnGate = new(1, 1);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern IntPtr GetStdHandle(int nStdHandle);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool SetStdHandle(int nStdHandle, IntPtr hHandle);

    public static async Task<ConPtyConnection> SpawnAsync(PtyOptions options)
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
}
