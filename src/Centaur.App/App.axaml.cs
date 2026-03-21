using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
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
        services.AddSingleton<IExtension, FpsOverlayExtension>();
        services.AddSingleton<NotificationServiceExtension>();
        services.AddSingleton<IExtension>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );
        services.AddSingleton<INotificationService>(sp =>
            sp.GetRequiredService<NotificationServiceExtension>()
        );
    }
}
