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
            desktop.MainWindow = new MainWindow(
                Services.GetRequiredService<TerminalServices>(),
                Services.GetRequiredService<NotificationServiceExtension>(),
                Services.GetRequiredService<SessionStore>()
            );
        }

        base.OnFrameworkInitializationCompleted();
    }

    static void ConfigureServices(IServiceCollection services)
    {
        AddHosting(services);
        AddOverlays(services);
        AddHistory(services);
        AddContextMenu(services);
        AddSettings(services);

        // The bundle every terminal pane is constructed with.
        services.AddSingleton(sp => new TerminalServices
        {
            Host = sp.GetRequiredService<ExtensionHost>(),
            Notifications = sp.GetRequiredService<INotificationService>(),
            Suggestions = sp.GetRequiredService<SuggestionState>(),
            CommandHistory = sp.GetRequiredService<CommandHistory>(),
            ReverseSearch = sp.GetRequiredService<ReverseSearchState>(),
            Settings = sp.GetRequiredService<Settings>(),
            Profiler = sp.GetRequiredService<RenderProfiler>(),
            FpsOverlay = sp.GetRequiredService<FpsOverlayExtension>(),
        });

        // Session (tabs/panes/window layout persistence)
        services.AddSingleton(sp =>
        {
            var path = AppDataPath("session.json");
            return new SessionStore(path, StorageErrorReporter(sp, "session layout", path));
        });
    }

    static void AddHosting(IServiceCollection services)
    {
        services.AddSingleton<ExtensionHost>();
        services.AddSingleton<IThemeProvider, CatppuccinThemeProvider>();
        services.AddSingleton<NotificationServiceExtension>();
        services.AddSingleton<IExtension>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );
        services.AddSingleton<INotificationService>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );
    }

    static void AddOverlays(IServiceCollection services)
    {
        services.AddSingleton<FpsOverlayExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<FpsOverlayExtension>());
        services.AddSingleton<RenderProfiler>();
        services.AddSingleton<ProfilerOverlayExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<ProfilerOverlayExtension>());
    }

    /// <summary>Shared command history, and the two features that read from it.</summary>
    static void AddHistory(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var path = AppDataPath("command-history.json");
            return new CommandHistory(path, StorageErrorReporter(sp, "command history", path));
        });

        services.AddSingleton<SuggestionState>();
        services.AddSingleton<SuggestionExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<SuggestionExtension>());
        services.AddSingleton<SuggestionOverlay>();
        services.AddSingleton<IProvider>(sp => sp.GetRequiredService<SuggestionOverlay>());

        services.AddSingleton<ReverseSearchState>();
        services.AddSingleton<ReverseSearchExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<ReverseSearchExtension>());
    }

    static void AddContextMenu(IServiceCollection services)
    {
        services.AddSingleton<IProvider, ClipboardMenuProvider>();
        services.AddSingleton<IProvider, ReadOnlyMenuProvider>();
        services.AddSingleton<IProvider, PaneMenuProvider>();
    }

    static void AddSettings(IServiceCollection services)
    {
        services.AddSingleton(sp =>
        {
            var path = AppDataPath("settings.json");
            return new Settings(path, StorageErrorReporter(sp, "settings", path));
        });
        services.AddSingleton<SettingsExtension>();
        services.AddSingleton<IExtension>(sp => sp.GetRequiredService<SettingsExtension>());
    }

    static string AppDataPath(string fileName)
    {
        return ConfigPaths.For(fileName);
    }

    /// <summary>
    /// Surfaces a storage failure as a toast instead of swallowing it. The path is included
    /// because the usual causes - a corrupt file, a locked file, a full disk - are all things
    /// the user can only act on if they know which file to look at.
    /// </summary>
    static Action<Exception> StorageErrorReporter(IServiceProvider sp, string what, string path)
    {
        return ex =>
            sp.GetRequiredService<INotificationService>()
                .Show(
                    $"Could not read or write {what}",
                    $"{path}: {ex.Message}. Delete or fix that file if the problem persists; "
                        + $"until then your {what} will not be saved.",
                    NotificationSeverity.Warning
                );
    }
}
