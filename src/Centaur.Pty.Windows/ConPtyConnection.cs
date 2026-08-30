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
    Task? outputPumpTask;
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

        // Start data pump tasks
        cts = new CancellationTokenSource();
        outputPumpTask = Task.Run(() => OutputPumpAsync(cts.Token));
        inputPumpTask = Task.Run(() => InputPumpAsync(cts.Token));
        processMonitorTask = Task.Run(() => ProcessMonitorAsync(cts.Token));
    }

    async Task OutputPumpAsync(CancellationToken ct)
    {
        if (pipeFromShellRead == null)
        {
            return;
        }

        var buffer = new byte[4096];
        var handle = pipeFromShellRead.DangerousGetHandle();

        try
        {
            while (!ct.IsCancellationRequested)
            {
                // Check if data is available
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
                    // Pipe error - likely closed
                    break;
                }

                if (available == 0)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                // Read available data
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
                    break;
                }

                if (bytesRead == 0)
                {
                    break;
                }

                await outputPipe.Writer.WriteAsync(
                    new ReadOnlyMemory<byte>(buffer, 0, bytesRead),
                    ct
                );
            }
        }
        catch (OperationCanceledException) { }
        catch { }
        finally
        {
            await outputPipe.Writer.CompleteAsync();
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
                    var data = segment.ToArray();
                    await Task.Run(
                        () =>
                        {
                            NativeMethods.WriteFile(handle, data, data.Length, out _, IntPtr.Zero);
                        },
                        ct
                    );
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

        if (outputPumpTask != null)
        {
            try
            {
                await outputPumpTask;
            }
            catch { }
        }
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

        await outputPipe.Reader.CompleteAsync();
        await outputPipe.Writer.CompleteAsync();
        await inputPipe.Reader.CompleteAsync();
        await inputPipe.Writer.CompleteAsync();

        if (hPC != IntPtr.Zero)
        {
            NativeMethods.ClosePseudoConsole(hPC);
        }

        pipeToShellWrite?.Dispose();
        pipeFromShellRead?.Dispose();
        processHandle?.Dispose();
        cts?.Dispose();
    }
}
