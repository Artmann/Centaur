using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using Xunit;

namespace Centaur.Tests;

public class FpsOverlayExtensionTests
{
    [Fact]
    public async Task ActivateAsync_InitializesResources()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new CatppuccinThemeProvider());
        var fps = new FpsOverlayExtension();
        host.RegisterExtension(fps);

        await host.ActivateAsync();

        // Extension is also a provider
        var overlay = host.GetProvider<IRenderOverlay>();
        Assert.NotNull(overlay);
        Assert.Same(fps, overlay);

        await host.DisposeAsync();
    }

    [Fact]
    public async Task DisposeAsync_CleansUpWithoutError()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new CatppuccinThemeProvider());
        var fps = new FpsOverlayExtension();
        host.RegisterExtension(fps);

        await host.ActivateAsync();
        await host.DisposeAsync();

        // Disposing again should not throw
        await fps.DisposeAsync();
    }

    [Fact]
    public void IsRegisteredAsBothExtensionAndProvider()
    {
        var fps = new FpsOverlayExtension();
        Assert.IsAssignableFrom<IExtension>(fps);
        Assert.IsAssignableFrom<IRenderOverlay>(fps);
        Assert.IsAssignableFrom<IProvider>(fps);
    }
}
