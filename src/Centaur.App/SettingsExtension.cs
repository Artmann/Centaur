using Centaur.Core.Hosting;
using Centaur.Core.Terminal;

namespace Centaur.App;

/// <summary>
/// Puts the user's settings on the event bus. Extensions that care about configuration - a
/// theme provider, an overlay that wants the font size - can then follow it without taking a
/// dependency on <see cref="Settings"/> itself, which is what the bus is for.
/// </summary>
public class SettingsExtension : IExtension
{
    readonly Settings settings;
    readonly TerminalServices services;
    Action<string>? changed;

    public SettingsExtension(Settings settings, TerminalServices services)
    {
        this.settings = settings;
        this.services = services;
    }

    public int Priority => 200;

    public Task ActivateAsync(IExtensionContext context)
    {
        changed = id =>
        {
            context.Events.Publish(new SettingsChangedEvent(id));

            if (id is SettingIds.Theme or "")
            {
                context.Events.Publish(new ThemeChangedEvent(services.Theme));
            }
        };

        settings.Changed += changed;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        if (changed != null)
        {
            settings.Changed -= changed;
        }

        return ValueTask.CompletedTask;
    }
}
