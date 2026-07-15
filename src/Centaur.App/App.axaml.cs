using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Centaur.App.Menus;
using Centaur.App.Menus.Providers;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Centaur.App;

public partial class App : Application
{
    public static ServiceProvider Services { get; private set; } = null!;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();
        Services.GetRequiredService<Settings>().Load();
        Services.GetRequiredService<SessionStore>().Load();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }

    static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ExtensionHost>();
        services.AddSingleton<IThemeProvider, CatppuccinThemeProvider>();
        services.AddSingleton<FpsOverlayExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<FpsOverlayExtension>());
        services.AddSingleton<RenderProfiler>();
        services.AddSingleton<ProfilerOverlayExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<ProfilerOverlayExtension>());
        services.AddSingleton<NotificationServiceExtension>();
        services.AddSingleton<IExtension>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );
        services.AddSingleton<INotificationService>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );

        // Shared command history
        services.AddSingleton(sp => new CommandHistory(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Centaur",
                "command-history.json"
            )
        ));

        // Suggestions
        services.AddSingleton<SuggestionState>();
        services.AddSingleton<SuggestionExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<SuggestionExtension>());
        services.AddSingleton<SuggestionOverlay>();
        services.AddSingleton<IProvider>(sp => sp.GetRequiredService<SuggestionOverlay>());

        // Reverse search
        services.AddSingleton<ReverseSearchState>();
        services.AddSingleton<ReverseSearchExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<ReverseSearchExtension>());

        // Context menu
        services.AddSingleton<IProvider, ClipboardMenuProvider>();
        services.AddSingleton<IProvider, ReadOnlyMenuProvider>();
        services.AddSingleton<IProvider, PaneMenuProvider>();

        // Settings
        services.AddSingleton(sp => new Settings(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Centaur",
                "settings.json"
            )
        ));
        services.AddSingleton<SettingsExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<SettingsExtension>());

        // Session (tabs/panes/window layout persistence)
        services.AddSingleton(sp => new SessionStore(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Centaur",
                "session.json"
            )
        ));
    }
}
