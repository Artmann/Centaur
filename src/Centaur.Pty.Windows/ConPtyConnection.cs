using System.IO.Pipelines;
using Centaur.Core.Pty;

namespace Centaur.Pty.Windows;

public class ConPtyConnection : IPtyConnection
{
    readonly Pipe outputPipe = new();
    readonly Pipe inputPipe = new();

    PseudoConsole? console;
    CancellationTokenSource? cts;
    Task? outputPumpTask;
    Task? inputPumpTask;
    Task? processMonitorTask;

    public PipeReader Output => outputPipe.Reader;
    public PipeWriter Input => inputPipe.Writer;
    public int ProcessId => console?.ProcessId ?? 0;

    ConPtyConnection() { }

    public static async Task<ConPtyConnection> CreateAsync(PtyOptions options)
    {
        var connection = new ConPtyConnection();
        await Task.Run(() => connection.Initialize(options));
        return connection;
    }

    void Initialize(PtyOptions options)
    {
        console = PseudoConsole.Start(options);

        cts = new CancellationTokenSource();
        outputPumpTask = Task.Run(() => OutputPumpAsync(cts.Token));
        inputPumpTask = Task.Run(() => InputPumpAsync(cts.Token));
        processMonitorTask = Task.Run(() => ProcessMonitorAsync(cts.Token));
    }

    async Task OutputPumpAsync(CancellationToken ct)
    {
        if (console == null)
        {
            return;
        }

        var buffer = new byte[4096];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var read = console.ReadOutput(buffer);
                if (read < 0)
                {
                    break;
                }
                if (read == 0)
                {
                    await Task.Delay(10, ct);
                    continue;
                }

                await outputPipe.Writer.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, read), ct);
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
        if (console == null)
        {
            return;
        }

        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await inputPipe.Reader.ReadAsync(ct);
                await console.WriteInputAsync(result.Buffer, ct);
                inputPipe.Reader.AdvanceTo(result.Buffer.End);

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
        if (console == null)
        {
            return;
        }

        var handle = console.ProcessHandle.DangerousGetHandle();

        try
        {
            await Task.Run(
                () =>
                {
                    while (!ct.IsCancellationRequested)
                    {
                        if (NativeMethods.WaitForSingleObject(handle, 100) == 0) // WAIT_OBJECT_0
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

    public void Resize(int columns, int rows) => console?.Resize(columns, rows);

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

        foreach (var task in new[] { outputPumpTask, inputPumpTask, processMonitorTask })
        {
            if (task == null)
            {
                continue;
            }

            try
            {
                await task;
            }
            catch { }
        }

        await outputPipe.Reader.CompleteAsync();
        await outputPipe.Writer.CompleteAsync();
        await inputPipe.Reader.CompleteAsync();
        await inputPipe.Writer.CompleteAsync();

        console?.Dispose();
        cts?.Dispose();
    }
}
