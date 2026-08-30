using System.Buffers;
using System.Runtime.InteropServices;
using Centaur.Core.Pty;
using Microsoft.Win32.SafeHandles;

namespace Centaur.Pty.Windows;

/// <summary>
/// Owns the Win32 half of a ConPTY session: the two pipe ends the terminal keeps, the pseudo
/// console itself and the shell process. A session either starts whole or not at all —
/// <see cref="Start"/> releases everything it opened before it throws.
/// </summary>
sealed class PseudoConsole : IDisposable
{
    IntPtr handle;

    PseudoConsole(
        IntPtr handle,
        SafeFileHandle shellInput,
        SafeFileHandle shellOutput,
        SafeFileHandle processHandle,
        int processId
    )
    {
        this.handle = handle;
        ShellInput = shellInput;
        ShellOutput = shellOutput;
        ProcessHandle = processHandle;
        ProcessId = processId;
    }

    /// <summary>The end we write to; the shell reads it as stdin.</summary>
    public SafeFileHandle ShellInput { get; }

    /// <summary>The end we read from; the shell writes it as stdout.</summary>
    public SafeFileHandle ShellOutput { get; }

    public SafeFileHandle ProcessHandle { get; }
    public int ProcessId { get; }

    public static PseudoConsole Start(PtyOptions options)
    {
        var childInput = IntPtr.Zero;
        var childOutput = IntPtr.Zero;
        SafeFileHandle? shellInput = null;
        SafeFileHandle? shellOutput = null;
        var console = IntPtr.Zero;

        try
        {
            CreatePipe(childReads: true, out childInput, out shellInput);
            CreatePipe(childReads: false, out childOutput, out shellOutput);
            console = CreateConsole(childInput, childOutput, options);
            var processHandle = StartProcess(console, options, out var processId);
            return new PseudoConsole(console, shellInput, shellOutput, processHandle, processId);
        }
        catch
        {
            if (console != IntPtr.Zero)
            {
                NativeMethods.ClosePseudoConsole(console);
            }
            shellInput?.Dispose();
            shellOutput?.Dispose();
            throw;
        }
        finally
        {
            // The pseudo console keeps its own copies of the child ends, so ours are spent
            // either way — on the failure path they are all that was ever opened.
            CloseIfOpen(childInput);
            CloseIfOpen(childOutput);
        }
    }

    /// <summary>Creates one inheritable pipe, handing back the end the child inherits and our
    /// own non-inheritable end. <paramref name="childReads"/> decides which end is which.</summary>
    static void CreatePipe(bool childReads, out IntPtr childEnd, out SafeFileHandle ourEnd)
    {
        var attributes = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        if (!NativeMethods.CreatePipe(out var read, out var write, ref attributes, 0))
        {
            var which = childReads ? "stdin" : "stdout";
            throw new InvalidOperationException($"Failed to create {which} pipe");
        }

        childEnd = childReads ? read : write;
        var ours = childReads ? write : read;
        NativeMethods.SetHandleInformation(ours, NativeMethods.HANDLE_FLAG_INHERIT, 0);
        ourEnd = new SafeFileHandle(ours, true);
    }

    static IntPtr CreateConsole(IntPtr childInput, IntPtr childOutput, PtyOptions options)
    {
        var size = new COORD { X = (short)options.columns, Y = (short)options.rows };
        var result = NativeMethods.CreatePseudoConsole(
            size,
            childInput,
            childOutput,
            0,
            out var console
        );
        if (result != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{result:X8}");
        }
        return console;
    }

    static SafeFileHandle StartProcess(IntPtr console, PtyOptions options, out int processId)
    {
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();
        startupInfo.lpAttributeList = AllocateAttributeList(console);

        try
        {
            if (
                !NativeMethods.CreateProcess(
                    null,
                    CommandLine(options),
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    NativeMethods.EXTENDED_STARTUPINFO_PRESENT,
                    IntPtr.Zero,
                    options.workingDirectory,
                    ref startupInfo,
                    out var processInfo
                )
            )
            {
                throw new InvalidOperationException(
                    $"CreateProcess failed: {Marshal.GetLastWin32Error()}"
                );
            }

            processId = processInfo.dwProcessId;
            NativeMethods.CloseHandle(processInfo.hThread);
            return new SafeFileHandle(processInfo.hProcess, true);
        }
        finally
        {
            NativeMethods.DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
            Marshal.FreeHGlobal(startupInfo.lpAttributeList);
        }
    }

    /// <summary>Builds the one-entry attribute list that ties the child to the pseudo console.
    /// The caller owns the returned list; a failure here frees it first.</summary>
    static IntPtr AllocateAttributeList(IntPtr console)
    {
        var size = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        var list = Marshal.AllocHGlobal(size);

        try
        {
            if (!NativeMethods.InitializeProcThreadAttributeList(list, 1, 0, ref size))
            {
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            }

            if (
                !NativeMethods.UpdateProcThreadAttribute(
                    list,
                    0,
                    (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    console,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero
                )
            )
            {
                NativeMethods.DeleteProcThreadAttributeList(list);
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");
            }

            return list;
        }
        catch
        {
            Marshal.FreeHGlobal(list);
            throw;
        }
    }

    static string CommandLine(PtyOptions options) =>
        options.arguments?.Length > 0
            ? options.executable + " " + string.Join(" ", options.arguments)
            : options.executable;

    static void CloseIfOpen(IntPtr handle)
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.CloseHandle(handle);
        }
    }

    /// <summary>Reads whatever the shell has waiting: the byte count, 0 while the pipe is idle,
    /// or -1 once it is closed or has errored.</summary>
    public int ReadOutput(byte[] buffer)
    {
        var handle = ShellOutput.DangerousGetHandle();
        if (
            !NativeMethods.PeekNamedPipe(
                handle,
                null,
                0,
                IntPtr.Zero,
                out var available,
                IntPtr.Zero
            )
        )
        {
            return -1;
        }

        if (available == 0)
        {
            return 0;
        }

        if (
            !NativeMethods.ReadFile(
                handle,
                buffer,
                Math.Min(buffer.Length, available),
                out var bytesRead,
                IntPtr.Zero
            )
        )
        {
            return -1;
        }

        return bytesRead == 0 ? -1 : bytesRead;
    }

    /// <summary>Writes a sequence to the shell's stdin. WriteFile blocks, so each segment goes
    /// out on the thread pool rather than on the caller's.</summary>
    public async Task WriteInputAsync(ReadOnlySequence<byte> buffer, CancellationToken ct)
    {
        var handle = ShellInput.DangerousGetHandle();
        foreach (var segment in buffer)
        {
            var data = segment.ToArray();
            await Task.Run(
                () =>
                {
                    NativeMethods.WriteFile(handle, data, data.Length, out _, IntPtr.Zero);
                },
                ct
            );
        }
    }

    public void Resize(int columns, int rows)
    {
        if (handle == IntPtr.Zero)
        {
            return;
        }

        var size = new COORD { X = (short)columns, Y = (short)rows };
        var result = NativeMethods.ResizePseudoConsole(handle, size);
        if (result != 0)
        {
            throw new InvalidOperationException($"ResizePseudoConsole failed: 0x{result:X8}");
        }
    }

    public void Dispose()
    {
        if (handle != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(handle);
            handle = IntPtr.Zero;
        }

        ShellInput.Dispose();
        ShellOutput.Dispose();
        ProcessHandle.Dispose();
    }
}
