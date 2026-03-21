namespace Centaur.Core.Hosting;

public class TerminalEventBus : ITerminalEvents
{
    readonly Dictionary<Type, List<Delegate>> handlers = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        var list = GetOrCreateList<TEvent>();
        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler)
    {
        var list = GetOrCreateList<TEvent>();
        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    public void Publish<TEvent>(TEvent evt)
    {
        if (!handlers.TryGetValue(typeof(TEvent), out var list)) return;
        foreach (var handler in list.ToArray())
        {
            if (handler is Action<TEvent> sync)
                sync(evt);
            else if (handler is Func<TEvent, Task> async_)
                async_(evt).GetAwaiter().GetResult();
        }
    }

    public async Task PublishAsync<TEvent>(TEvent evt)
    {
        if (!handlers.TryGetValue(typeof(TEvent), out var list)) return;
        foreach (var handler in list.ToArray())
        {
            if (handler is Action<TEvent> sync)
                sync(evt);
            else if (handler is Func<TEvent, Task> async_)
                await async_(evt);
        }
    }

    List<Delegate> GetOrCreateList<TEvent>()
    {
        var key = typeof(TEvent);
        if (!handlers.TryGetValue(key, out var list))
        {
            list = [];
            handlers[key] = list;
        }
        return list;
    }

    sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
