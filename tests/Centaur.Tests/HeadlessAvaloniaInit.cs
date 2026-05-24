using Avalonia;
using Avalonia.Headless;
using Centaur.Tests;

[assembly: AvaloniaTestApplication(typeof(TestAppBuilder))]

namespace Centaur.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions());
}
