namespace Centaur.Core.Hosting;

public interface IExtension : IAsyncDisposable
{
    Task ActivateAsync(IExtensionContext context);
}
