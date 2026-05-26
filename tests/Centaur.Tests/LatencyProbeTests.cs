using Centaur.Core.Terminal;
using Xunit;

namespace Centaur.Tests;

public class LatencyProbeTests
{
    [Fact]
    public void Version_StartsAtZero()
    {
        var probe = new LatencyProbe();

        Assert.Equal(0, probe.Version);
    }

    [Fact]
    public void Bump_AdvancesVersion()
    {
        var probe = new LatencyProbe();

        probe.Bump();

        Assert.Equal(1, probe.Version);
    }

    [Fact]
    public async Task Bump_IsAtomicUnderConcurrency()
    {
        var probe = new LatencyProbe();
        const int tasksCount = 16;
        const int bumpsPerTask = 10_000;

        var tasks = new Task[tasksCount];
        for (var i = 0; i < tasksCount; i++)
        {
            tasks[i] = Task.Run(() =>
            {
                for (var j = 0; j < bumpsPerTask; j++)
                {
                    probe.Bump();
                }
            });
        }
        await Task.WhenAll(tasks);

        Assert.Equal(tasksCount * bumpsPerTask, probe.Version);
    }
}
