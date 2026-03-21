namespace Centaur.Core.Hosting;

public interface ITerminalEvents
{
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
    IDisposable Subscribe<TEvent>(Func<TEvent, Task> handler);
    void Publish<TEvent>(TEvent evt);
    Task PublishAsync<TEvent>(TEvent evt);
}
