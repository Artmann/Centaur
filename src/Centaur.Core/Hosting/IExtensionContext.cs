namespace Centaur.Core.Hosting;

public interface IExtensionContext
{
    IReadOnlyList<T> GetProviders<T>() where T : class;
    T? GetProvider<T>() where T : class;
    ITerminalEvents Events { get; }
}
