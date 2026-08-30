using System.Text;
using Centaur.Core.Pty;
using Centaur.Pty.Windows;
using Xunit;

namespace Centaur.Tests;

public class ConPtyConnectionTests
{
    [Fact]
    public async Task CreateAsync_SpawnsPowerShell()
    {
        var options = new PtyOptions(executable: "powershell.exe", columns: 80, rows: 24);

        await using var pty = await ConPtyConnection.CreateAsync(options);

        Assert.True(pty.ProcessId > 0);
    }

    [Fact]
    public async Task CreateAsync_ReceivesInitialOutput()
    {
        var options = new PtyOptions(executable: "cmd.exe", columns: 80, rows: 24);
        await using var pty = await ConPtyConnection.CreateAsync(options);

        // Wait a moment for initial output
        await Task.Delay(500);

        // Try to read any available output
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        var output = new StringBuilder();

        try
        {
            var result = await pty.Output.ReadAsync(cts.Token);
            if (result.Buffer.Length > 0)
            {
                output.Append(Encoding.UTF8.GetString(result.Buffer));
                pty.Output.AdvanceTo(result.Buffer.End);
            }
        }
        catch (OperationCanceledException) { }

        // Should receive at least some output (VT sequences from ConPTY)
        Assert.True(output.Length > 0, "Expected to receive some initial output from PTY");
    }

    [Fact]
    public async Task Input_CanWriteData()
    {
        var options = new PtyOptions(executable: "cmd.exe", columns: 80, rows: 24);
        await using var pty = await ConPtyConnection.CreateAsync(options);

        // Writing should not throw
        await pty.Input.WriteAsync("test\r"u8.ToArray());
        await pty.Input.FlushAsync();

        Assert.True(true);
    }

    [Fact]
    public async Task Resize_DoesNotThrow()
    {
        var options = new PtyOptions(executable: "cmd.exe", columns: 80, rows: 24);
        await using var pty = await ConPtyConnection.CreateAsync(options);

        // Should not throw
        pty.Resize(120, 40);

        Assert.True(true);
    }

    [Fact]
    public async Task CreateAsync_WithMissingExecutable_Throws()
    {
        var options = new PtyOptions(
            executable: "centaur-no-such-program.exe",
            columns: 80,
            rows: 24
        );

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConPtyConnection.CreateAsync(options)
        );

        Assert.Contains("CreateProcess failed", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateAsync_WithMissingWorkingDirectory_Throws()
    {
        var options = new PtyOptions(
            executable: "cmd.exe",
            columns: 80,
            rows: 24,
            workingDirectory: Path.Combine(Path.GetTempPath(), "centaur-no-such-directory")
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ConPtyConnection.CreateAsync(options)
        );
    }

    /// <summary>A failed startup must release everything it opened. Each leaked pseudo console
    /// keeps handles (and an OpenConsole process) alive, so the count would climb per attempt.</summary>
    [Fact]
    public async Task CreateAsync_WhenProcessCreationFails_LeaksNoHandles()
    {
        const int attempts = 40;
        var options = new PtyOptions(
            executable: "centaur-no-such-program.exe",
            columns: 80,
            rows: 24
        );

        // Warm up first: the first few attempts pull in pages, threads and JIT state.
        await FailToCreate(options, 5);
        var before = CurrentHandleCount();

        await FailToCreate(options, attempts);
        var after = CurrentHandleCount();

        Assert.True(
            after - before < attempts,
            $"handle count grew from {before} to {after} over {attempts} failed startups"
        );
    }

    static async Task FailToCreate(PtyOptions options, int attempts)
    {
        for (var i = 0; i < attempts; i++)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                ConPtyConnection.CreateAsync(options)
            );
        }

        // Handles owned by the discarded connections are released by their finalizers.
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    static int CurrentHandleCount()
    {
        using var self = System.Diagnostics.Process.GetCurrentProcess();
        self.Refresh();
        return self.HandleCount;
    }
}
