namespace Centaur.Core.Hosting;

public class ExtensionHost : IExtensionContext, IAsyncDisposable
{
    readonly List<IProvider> providers = [];
    readonly List<IExtension> extensions = [];
    readonly TerminalEventBus events = new();
    bool activated;

    public ITerminalEvents Events => events;

    public ExtensionHost() { }

    public ExtensionHost(IEnumerable<IProvider> providers, IEnumerable<IExtension> extensions)
    {
        foreach (var provider in providers)
        {
            this.providers.Add(provider);
        }

        foreach (var extension in extensions)
        {
            this.extensions.Add(extension);
            if (extension is IProvider provider)
            {
                this.providers.Add(provider);
            }
        }
    }

    public ExtensionHost RegisterProvider<T>(T provider)
        where T : class, IProvider
    {
        if (activated)
        {
            throw new InvalidOperationException("Cannot register after activation.");
        }

        providers.Add(provider);
        return this;
    }

    public ExtensionHost RegisterExtension(IExtension extension)
    {
        if (activated)
        {
            throw new InvalidOperationException("Cannot register after activation.");
        }

        extensions.Add(extension);
        if (extension is IProvider provider)
        {
            providers.Add(provider);
        }

        return this;
    }

    public async Task ActivateAsync()
    {
        activated = true;
        providers.Sort((a, b) => a.Priority.CompareTo(b.Priority));

        foreach (var ext in extensions)
        {
            await ext.ActivateAsync(this);
        }

        await events.PublishAsync(new TerminalReadyEvent());
    }

    public IReadOnlyList<T> GetProviders<T>()
        where T : class =>
        providers
            .OfType<T>()
            .OrderBy(p => (p as IProvider)?.Priority ?? 1000)
            .ToList()
            .AsReadOnly();

    public T? GetProvider<T>()
        where T : class =>
        providers.OfType<T>().OrderBy(p => (p as IProvider)?.Priority ?? 1000).FirstOrDefault();

    public async ValueTask DisposeAsync()
    {
        await events.PublishAsync(new TerminalShutdownEvent());

        for (int i = extensions.Count - 1; i >= 0; i--)
        {
            await extensions[i].DisposeAsync();
        }

        extensions.Clear();
        providers.Clear();
    }
}
