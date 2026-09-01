using Centaur.App;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using CentaurApp = Centaur.App.App;

namespace Centaur.Tests;

/// <summary>
/// The application's service graph, resolved the way startup resolves it.
///
/// A cycle in it does not throw. The container cannot see through a factory lambda, so a
/// service whose factory resolves back into one already being constructed re-enters the
/// container's own cache for the same key and deadlocks there - and the app then starts,
/// stays alive, and never shows a window, with nothing on stderr to say why.
///
/// That is why this resolves on a background thread with a deadline instead of just calling
/// GetRequiredService: a deadlock has to fail the test rather than hang the run.
/// </summary>
public class AppServiceGraphTests
{
    [Fact]
    public void The_services_startup_resolves_all_resolve_without_a_cycle()
    {
        var finished = ResolveWithDeadline(provider =>
        {
            // Exactly what App.OnFrameworkInitializationCompleted asks for, in its order.
            provider.GetRequiredService<Settings>();
            provider.GetRequiredService<SessionStore>();
            provider.GetRequiredService<TerminalServices>();
            provider.GetRequiredService<NotificationServiceExtension>();
        });

        Assert.True(
            finished,
            "Resolving the startup services did not finish within 10 seconds, which means the "
                + "container is deadlocked on a dependency cycle."
        );
    }

    /// <summary>The extension host pulls in every registered extension and provider, so
    /// resolving it exercises the parts of the graph the window never asks for directly.</summary>
    [Fact]
    public void Every_extension_and_provider_resolves()
    {
        var finished = ResolveWithDeadline(provider =>
        {
            var host = provider.GetRequiredService<ExtensionHost>();
            Assert.NotNull(host.GetProvider<IThemeProvider>());
        });

        Assert.True(
            finished,
            "Resolving the extension host did not finish within 10 seconds, which means the "
                + "container is deadlocked on a dependency cycle."
        );
    }

    /// <summary>
    /// Runs <paramref name="resolve"/> against a real provider on a background thread, and
    /// reports whether it finished. The thread is left behind if it deadlocks - it is a
    /// background thread, so it does not hold the test run open.
    /// </summary>
    static bool ResolveWithDeadline(Action<IServiceProvider> resolve)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                RunAgainstRealProvider(resolve);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        })
        {
            IsBackground = true,
        };

        thread.Start();
        var finished = thread.Join(TimeSpan.FromSeconds(10));

        if (failure != null)
        {
            throw new InvalidOperationException(
                "Resolving the application's services threw.",
                failure
            );
        }

        return finished;
    }

    /// <summary>Builds the real container the way startup builds it, runs
    /// <paramref name="resolve"/> against it, and tears it down again.</summary>
    static void RunAgainstRealProvider(Action<IServiceProvider> resolve)
    {
        var services = new ServiceCollection();
        CentaurApp.ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        try
        {
            resolve(provider);
        }
        finally
        {
            // The host is IAsyncDisposable only, which the container refuses to dispose
            // synchronously. Blocking here is safe: this is a bare thread with no
            // synchronisation context to deadlock against.
            provider.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
    }
}
