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
}
