using System.IO.Pipelines;
using System.Runtime.InteropServices;
using Centaur.Core.Pty;
using Microsoft.Win32.SafeHandles;

namespace Centaur.Pty.Windows;

public class ConPtyConnection : IPtyConnection
{
    readonly Pipe outputPipe = new();
    readonly Pipe inputPipe = new();

    IntPtr hPC;
    SafeFileHandle? processHandle;
    SafeFileHandle? pipeToShellWrite;
    SafeFileHandle? pipeFromShellRead;
    CancellationTokenSource? cts;
    Thread? outputPumpThread;
    Task? inputPumpTask;
    Task? processMonitorTask;

    public PipeReader Output => outputPipe.Reader;
    public PipeWriter Input => inputPipe.Writer;
    public int ProcessId { get; private set; }

    ConPtyConnection() { }

    public static async Task<ConPtyConnection> CreateAsync(PtyOptions options)
    {
        var connection = new ConPtyConnection();
        await Task.Run(() => connection.Initialize(options));
        return connection;
    }

    unsafe void Initialize(PtyOptions options)
    {
        // Create pipes for PTY communication
        var sa = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = true,
        };

        // Pipe: Terminal writes -> Shell reads (stdin)
        if (
            !NativeMethods.CreatePipe(
                out var pipeToShellRead,
                out var pipeToShellWriteHandle,
                ref sa,
                0
            )
        )
        {
            throw new InvalidOperationException("Failed to create stdin pipe");
        }

        // Pipe: Shell writes -> Terminal reads (stdout)
        if (
            !NativeMethods.CreatePipe(
                out var pipeFromShellReadHandle,
                out var pipeFromShellWrite,
                ref sa,
                0
            )
        )
        {
            NativeMethods.CloseHandle(pipeToShellRead);
            NativeMethods.CloseHandle(pipeToShellWriteHandle);
            throw new InvalidOperationException("Failed to create stdout pipe");
        }

        // Make our ends of the pipes non-inheritable
        NativeMethods.SetHandleInformation(
            pipeToShellWriteHandle,
            NativeMethods.HANDLE_FLAG_INHERIT,
            0
        );
        NativeMethods.SetHandleInformation(
            pipeFromShellReadHandle,
            NativeMethods.HANDLE_FLAG_INHERIT,
            0
        );

        // Store handles for managed access
        pipeToShellWrite = new SafeFileHandle(pipeToShellWriteHandle, true);
        pipeFromShellRead = new SafeFileHandle(pipeFromShellReadHandle, true);

        // Create the pseudo console
        var size = new COORD { X = (short)options.columns, Y = (short)options.rows };
        var result = NativeMethods.CreatePseudoConsole(
            size,
            pipeToShellRead,
            pipeFromShellWrite,
            0,
            out hPC
        );
        if (result != 0)
        {
            throw new InvalidOperationException($"CreatePseudoConsole failed: 0x{result:X8}");
        }

        // Close the child-side handles (now owned by pseudo console)
        NativeMethods.CloseHandle(pipeToShellRead);
        NativeMethods.CloseHandle(pipeFromShellWrite);

        // Set up process creation with pseudo console
        var startupInfo = new STARTUPINFOEX();
        startupInfo.StartupInfo.cb = Marshal.SizeOf<STARTUPINFOEX>();

        // Initialize the attribute list
        var attrListSize = IntPtr.Zero;
        NativeMethods.InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attrListSize);

        startupInfo.lpAttributeList = Marshal.AllocHGlobal(attrListSize);
        try
        {
            if (
                !NativeMethods.InitializeProcThreadAttributeList(
                    startupInfo.lpAttributeList,
                    1,
                    0,
                    ref attrListSize
                )
            )
            {
                throw new InvalidOperationException("InitializeProcThreadAttributeList failed");
            }

            // Add pseudo console attribute
            if (
                !NativeMethods.UpdateProcThreadAttribute(
                    startupInfo.lpAttributeList,
                    0,
                    (IntPtr)NativeMethods.PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                    hPC,
                    (IntPtr)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero
                )
            )
            {
                throw new InvalidOperationException("UpdateProcThreadAttribute failed");
            }

            // Build command line
            var commandLine = options.executable;
            if (options.arguments?.Length > 0)
            {
                commandLine += " " + string.Join(" ", options.arguments);
            }

            // Create the process
            var processInfo = new PROCESS_INFORMATION();
            var creationFlags = NativeMethods.EXTENDED_STARTUPINFO_PRESENT;

            if (
                !NativeMethods.CreateProcess(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    creationFlags,
                    IntPtr.Zero,
                    options.workingDirectory,
                    ref startupInfo,
                    out processInfo
                )
            )
            {
                throw new InvalidOperationException(
                    $"CreateProcess failed: {Marshal.GetLastWin32Error()}"
                );
            }

            ProcessId = processInfo.dwProcessId;
            processHandle = new SafeFileHandle(processInfo.hProcess, true);
            NativeMethods.CloseHandle(processInfo.hThread);

            NativeMethods.DeleteProcThreadAttributeList(startupInfo.lpAttributeList);
        }
        finally
        {
            Marshal.FreeHGlobal(startupInfo.lpAttributeList);
        }

        // Start data pumps. The output pump runs on a dedicated thread doing a blocking
        // ReadFile so echoed bytes are picked up the instant the shell writes them (no poll
        // delay); the input/monitor pumps stay on the thread pool since they await cleanly.
        cts = new CancellationTokenSource();
        outputPumpThread = new Thread(OutputPumpLoop)
        {
            IsBackground = true,
            Name = "conpty-output-pump",
        };
        outputPumpThread.Start();
        inputPumpTask = Task.Run(() => InputPumpAsync(cts.Token));
        processMonitorTask = Task.Run(() => ProcessMonitorAsync(cts.Token));
    }

    void OutputPumpLoop()
    {
        if (pipeFromShellRead == null)
        {
            return;
        }

        var buffer = new byte[4096];
        var handle = pipeFromShellRead.DangerousGetHandle();

        try
        {
            while (true)
            {
                // Blocking read: returns the moment the shell writes, with zero polling
                // latency. ReadFile cannot observe the cancellation token; during teardown
                // DisposeAsync closes this handle, which makes the in-flight ReadFile return
                // false and breaks the loop.
                if (
                    !NativeMethods.ReadFile(
                        handle,
                        buffer,
                        buffer.Length,
                        out var bytesRead,
                        IntPtr.Zero
                    )
                )
                {
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                // Hand off to the pipe synchronously; pipe backpressure correctly blocks
                // this dedicated thread when the reader falls behind.
                outputPipe
                    .Writer.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead))
                    .AsTask()
                    .GetAwaiter()
                    .GetResult();
            }
        }
        catch
        {
            // A broken/disposed pipe during teardown is the normal exit — nothing to report.
        }
        finally
        {
            outputPipe.Writer.Complete();
        }
    }

    async Task InputPumpAsync(CancellationToken ct)
    {
        if (pipeToShellWrite == null)
        {
            return;
        }

        var handle = pipeToShellWrite.DangerousGetHandle();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await inputPipe.Reader.ReadAsync(ct);
                var buffer = result.Buffer;

                foreach (var segment in buffer)
                {
                    // This pump already runs on its own background task, so write synchronously
                    // inline — wrapping each segment in a Task.Run only added a thread hop (and
                    // thus latency) to every keystroke.
                    var data = segment.ToArray();
                    NativeMethods.WriteFile(handle, data, data.Length, out _, IntPtr.Zero);
                }

                inputPipe.Reader.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { }
        finally
        {
            await inputPipe.Reader.CompleteAsync();
        }
    }

    async Task ProcessMonitorAsync(CancellationToken ct)
    {
        if (processHandle == null)
        {
            return;
        }

        try
        {
            await Task.Run(
                () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var waitResult = NativeMethods.WaitForSingleObject(
                            processHandle.DangerousGetHandle(),
                            100
                        );
                        if (waitResult == 0) // WAIT_OBJECT_0
                        {
                            break;
                        }
                    }
                },
                ct
            );
        }
        catch (OperationCanceledException) { }
    }

    public void Resize(int columns, int rows)
    {
        if (hPC != IntPtr.Zero)
        {
            var size = new COORD { X = (short)columns, Y = (short)rows };
            var result = NativeMethods.ResizePseudoConsole(hPC, size);
            if (result != 0)
            {
                throw new InvalidOperationException($"ResizePseudoConsole failed: 0x{result:X8}");
            }
        }
    }

    public async Task WaitForExitAsync(CancellationToken ct = default)
    {
        if (processMonitorTask != null)
        {
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                cts?.Token ?? CancellationToken.None
            );
            try
            {
                await processMonitorTask.WaitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) { }
        }
    }

    public async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        cts?.Cancel();

        // The input/monitor pumps await on the cancellation token, so they stop on their own.
        if (inputPumpTask != null)
        {
            try
            {
                await inputPumpTask;
            }
            catch { }
        }
        if (processMonitorTask != null)
        {
            try
            {
                await processMonitorTask;
            }
            catch { }
        }

        // Unblock the output pump, which can't observe the token. It is parked in one of two
        // places: a WriteAsync stalled because nobody is draining outputPipe, or a blocking
        // ReadFile. Completing the reader frees a stalled WriteAsync. For the blocking ReadFile,
        // closing our *read* handle does NOT reliably wake it (an anonymous-pipe ReadFile only
        // returns on data or when every *write* handle is closed) — so we tear down the
        // pseudoconsole, which makes conhost close the pipe's write end and the ReadFile then
        // returns false. With both done the loop exits, so the bounded join won't time out (and
        // IsBackground guarantees a stuck thread can never block process exit regardless).
        await outputPipe.Reader.CompleteAsync();
        if (hPC != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(hPC);
            hPC = IntPtr.Zero;
        }
        outputPumpThread?.Join(TimeSpan.FromSeconds(2));

        // The pump owns and completes outputPipe.Writer in its finally; these are idempotent.
        await outputPipe.Writer.CompleteAsync();
        await inputPipe.Reader.CompleteAsync();
        await inputPipe.Writer.CompleteAsync();

        pipeFromShellRead?.Dispose();
        pipeToShellWrite?.Dispose();
        processHandle?.Dispose();
        cts?.Dispose();
    }
}

[StructLayout(LayoutKind.Sequential)]
struct COORD
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
struct SECURITY_ATTRIBUTES
{
    public int nLength;
    public IntPtr lpSecurityDescriptor;

    [MarshalAs(UnmanagedType.Bool)]
    public bool bInheritHandle;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct STARTUPINFOEX
{
    public STARTUPINFO StartupInfo;
    public IntPtr lpAttributeList;
}

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
struct STARTUPINFO
{
    public int cb;
    public string lpReserved;
    public string lpDesktop;
    public string lpTitle;
    public int dwX;
    public int dwY;
    public int dwXSize;
    public int dwYSize;
    public int dwXCountChars;
    public int dwYCountChars;
    public int dwFillAttribute;
    public int dwFlags;
    public short wShowWindow;
    public short cbReserved2;
    public IntPtr lpReserved2;
    public IntPtr hStdInput;
    public IntPtr hStdOutput;
    public IntPtr hStdError;
}

[StructLayout(LayoutKind.Sequential)]
struct PROCESS_INFORMATION
{
    public IntPtr hProcess;
    public IntPtr hThread;
    public int dwProcessId;
    public int dwThreadId;
}

static class NativeMethods
{
    public const int EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    public const int HANDLE_FLAG_INHERIT = 0x00000001;
    public const int PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CreatePipe(
        out IntPtr hReadPipe,
        out IntPtr hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetHandleInformation(IntPtr hObject, int dwMask, int dwFlags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int CreatePseudoConsole(
        COORD size,
        IntPtr hInput,
        IntPtr hOutput,
        int dwFlags,
        out IntPtr phPC
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool InitializeProcThreadAttributeList(
        IntPtr lpAttributeList,
        int dwAttributeCount,
        int dwFlags,
        ref IntPtr lpSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool UpdateProcThreadAttribute(
        IntPtr lpAttributeList,
        int dwFlags,
        IntPtr attribute,
        IntPtr lpValue,
        IntPtr cbSize,
        IntPtr lpPreviousValue,
        IntPtr lpReturnSize
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool CreateProcess(
        string? lpApplicationName,
        string lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        int dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int WaitForSingleObject(IntPtr hHandle, int dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToRead,
        out int lpNumberOfBytesRead,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(
        IntPtr hFile,
        byte[] lpBuffer,
        int nNumberOfBytesToWrite,
        out int lpNumberOfBytesWritten,
        IntPtr lpOverlapped
    );

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool PeekNamedPipe(
        IntPtr hNamedPipe,
        byte[]? lpBuffer,
        int nBufferSize,
        IntPtr lpBytesRead,
        out int lpTotalBytesAvail,
        IntPtr lpBytesLeftThisMessage
    );
}
