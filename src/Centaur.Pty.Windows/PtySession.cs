using System.Buffers;
using Centaur.Core.Pty;

namespace Centaur.Pty.Windows;

/// <summary>
/// A running pseudoconsole together with the background loop that drains its output.
///
/// Callers get bytes through <paramref name="onOutput"/> and are told once, through
/// <paramref name="onExited"/>, when the child is gone. Writes are serialized here because
/// input can come from the UI thread and from the parser answering a query on the read
/// thread at the same time, and their write/flush pairs must not interleave.
/// </summary>
public sealed class PtySession : IAsyncDisposable
{
    readonly ConPtyConnection connection;
    readonly Action<ReadOnlySequence<byte>> onOutput;
    readonly Action? onExited;
    readonly SemaphoreSlim writeLock = new(1, 1);
    readonly CancellationTokenSource readCts = new();
    readonly Task readTask;

    PtySession(
        ConPtyConnection connection,
        Action<ReadOnlySequence<byte>> onOutput,
        Action? onExited
    )
    {
        this.connection = connection;
        this.onOutput = onOutput;
        this.onExited = onExited;
        readTask = Task.Run(() => ReadLoopAsync(readCts.Token));
    }

    /// <param name="onOutput">
    /// Called on the read thread with each chunk the child produced. It runs inside the
    /// read loop, so it should hand off rather than block.
    /// </param>
    /// <param name="spawn">
    /// How to create the connection, for callers that need to prepare the process
    /// environment first. Defaults to <see cref="ConPtyConnection.CreateAsync"/>.
    /// </param>
    public static async Task<PtySession> StartAsync(
        PtyOptions options,
        Action<ReadOnlySequence<byte>> onOutput,
        Action? onExited = null,
        Func<PtyOptions, Task<ConPtyConnection>>? spawn = null
    )
    {
        var connection = await (spawn ?? ConPtyConnection.CreateAsync)(options);
        return new PtySession(connection, onOutput, onExited);
    }

    public int ProcessId => connection.ProcessId;

    public void Resize(int columns, int rows) => connection.Resize(columns, rows);

    /// <summary>Writes to the child's stdin, waiting for any write already in flight.</summary>
    public async Task WriteAsync(byte[] data)
    {
        await writeLock.WaitAsync();
        try
        {
            await connection.Input.WriteAsync(data);
            await connection.Input.FlushAsync();
        }
        finally
        {
            writeLock.Release();
        }
    }

    async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await connection.Output.ReadAsync(ct);
                var buffer = result.Buffer;

                onOutput(buffer);

                connection.Output.AdvanceTo(buffer.End);

                if (result.IsCompleted)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on dispose; the child is not gone of its own accord.
            return;
        }
        catch (Exception)
        {
            // Shell exited or the pipe closed. Either way the session is over, and
            // onExited is how the caller finds out.
        }

        onExited?.Invoke();
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
            // The loop swallows its own failures; this only observes cancellation.
        }

        await connection.DisposeAsync();
        readCts.Dispose();
        writeLock.Dispose();
    }
}
