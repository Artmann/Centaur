namespace Centaur.Core.Hosting;

public class TerminalEventBus : ITerminalEvents
{
    readonly Dictionary<Type, List<Delegate>> handlers = [];

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler) => Add<TEvent>(handler);

    public IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler) => Add<TEvent>(handler);

    public void Publish<TEvent>(TEvent evt)
    {
        foreach (var handler in HandlersFor<TEvent>())
        {
            if (handler is Action<TEvent> sync)
            {
                sync(evt);
            }
            else if (handler is Func<TEvent, Task> async_)
            {
                async_(evt).GetAwaiter().GetResult();
            }
        }
    }

    public async Task PublishAsync<TEvent>(TEvent evt)
    {
        foreach (var handler in HandlersFor<TEvent>())
        {
            if (handler is Action<TEvent> sync)
            {
                sync(evt);
            }
            else if (handler is Func<TEvent, Task> async_)
            {
                await async_(evt);
            }
        }
    }

    Subscription Add<TEvent>(Delegate handler)
    {
        var key = typeof(TEvent);
        if (!handlers.TryGetValue(key, out var list))
        {
            list = [];
            handlers[key] = list;
        }

        list.Add(handler);
        return new Subscription(() => list.Remove(handler));
    }

    /// <summary>
    /// Snapshots the handler list so a handler that subscribes or unsubscribes while being
    /// invoked cannot mutate the collection we are iterating.
    /// </summary>
    Delegate[] HandlersFor<TEvent>()
    {
        return handlers.TryGetValue(typeof(TEvent), out var list) ? list.ToArray() : [];
    }

    sealed class Subscription(Action unsubscribe) : IDisposable
    {
        public void Dispose() => unsubscribe();
    }
}
