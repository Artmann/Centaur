using Centaur.Core.Hosting;

namespace Centaur.App;

public class SettingsExtension : IExtension
{
    IDisposable? settingsSub;

    public int Priority => 200;

    public Task ActivateAsync(IExtensionContext context)
    {
        settingsSub = context.Events.Subscribe<SettingsRequestedEvent>(_ => { });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        settingsSub?.Dispose();
        return ValueTask.CompletedTask;
    }
}
