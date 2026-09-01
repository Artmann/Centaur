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
    readonly Func<TerminalTheme> theme;
    Action<string>? changed;

    /// <param name="theme">The active theme, read on demand rather than injected. The type that
    /// resolves it depends on the <see cref="ExtensionHost"/> this extension is registered in,
    /// so taking it directly would close a cycle in the container - and because the container
    /// does not detect cycles through factory lambdas, that cycle recursed at startup instead of
    /// throwing, and the window was never built.</param>
    public SettingsExtension(Settings settings, Func<TerminalTheme> theme)
    {
        this.settings = settings;
        this.theme = theme;
    }

    public int Priority => 200;

    public Task ActivateAsync(IExtensionContext context)
    {
        changed = id =>
        {
            context.Events.Publish(new SettingsChangedEvent(id));

            if (id is SettingIds.Theme or "")
            {
                context.Events.Publish(new ThemeChangedEvent(theme()));
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
