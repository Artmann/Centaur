using System.IO.Pipelines;

namespace Centaur.Core.Pty;

public record PtyOptions(
    string executable = "cmd.exe",
    string[]? arguments = null,
    string? workingDirectory = null,
    int columns = 80,
    int rows = 24
);

public interface IPtyConnection : IAsyncDisposable
{
    PipeReader Output { get; }
    PipeWriter Input { get; }
    int ProcessId { get; }

    void Resize(int columns, int rows);
    Task WaitForExitAsync(CancellationToken ct = default);
}
