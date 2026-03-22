using Centaur.Core.Hosting;

namespace Centaur.App;

public class ReverseSearchExtension : IExtension
{
    IDisposable? searchSub;

    public int Priority => 200;

    public Task ActivateAsync(IExtensionContext context)
    {
        searchSub = context.Events.Subscribe<ReverseSearchRequestedEvent>(_ => { });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        searchSub?.Dispose();
        return ValueTask.CompletedTask;
    }
}
