using System.IO.Pipelines;
using Centaur.Core.Pty;

namespace Centaur.Pty.Windows;

public class ConPtyConnection : IPtyConnection
{
    readonly Pipe outputPipe = new();
    readonly Pipe inputPipe = new();

    public PipeReader Output => outputPipe.Reader;
    public PipeWriter Input => inputPipe.Writer;
    public int ProcessId { get; private set; }

    ConPtyConnection() { }

    public static async Task<ConPtyConnection> CreateAsync(PtyOptions options)
    {
        var connection = new ConPtyConnection();
        // TODO: Implement ConPTY creation
        await Task.CompletedTask;
        return connection;
    }

    public void Resize(int columns, int rows)
    {
        // TODO: Call ResizePseudoConsole
    }

    public Task WaitForExitAsync(CancellationToken ct = default)
    {
        // TODO: Wait for process exit
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await outputPipe.Reader.CompleteAsync();
        await outputPipe.Writer.CompleteAsync();
        await inputPipe.Reader.CompleteAsync();
        await inputPipe.Writer.CompleteAsync();
        // TODO: Close handles
    }
}
