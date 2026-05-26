using System.Diagnostics;
using Centaur.Core.Hosting;
using Centaur.Core.Terminal;
using Centaur.Rendering;
using SkiaSharp;
using Xunit;

namespace Centaur.Tests;

public class FpsOverlayExtensionTests
{
    [Fact]
    public async Task ActivateAsync_InitializesResources()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new CatppuccinThemeProvider());
        var fps = new FpsOverlayExtension(new LatencyProbe());
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
        var fps = new FpsOverlayExtension(new LatencyProbe());
        host.RegisterExtension(fps);

        await host.ActivateAsync();
        await host.DisposeAsync();

        // Disposing again should not throw
        await fps.DisposeAsync();
    }

    [Fact]
    public void IsRegisteredAsBothExtensionAndProvider()
    {
        var fps = new FpsOverlayExtension(new LatencyProbe());
        Assert.IsAssignableFrom<IExtension>(fps);
        Assert.IsAssignableFrom<IRenderOverlay>(fps);
        Assert.IsAssignableFrom<IProvider>(fps);
    }

    [Fact]
    public async Task Render_RecordsLatency_WhenKeystrokeEchoes()
    {
        var probe = new LatencyProbe();
        var host = new ExtensionHost();
        host.RegisterProvider(new CatppuccinThemeProvider());
        var fps = new FpsOverlayExtension(probe);
        host.RegisterExtension(fps);
        await host.ActivateAsync();

        var theme = CatppuccinThemes.Macchiato;
        using var surface = SKSurface.Create(new SKImageInfo(800, 600));
        using var typeface = SKTypeface.Default;
        using var font = new SKFont(typeface, 14);

        // A keystroke was sent a known time ago, then the shell echo was parsed (probe bumped).
        var sentAt = Stopwatch.GetTimestamp();
        host.Events.Publish(new KeystrokeSentEvent(sentAt));
        probe.Bump();

        // No throw, and the rendered frame records a measured latency.
        fps.Render(surface.Canvas, 800, theme, font, typeface);

        Assert.True(fps.LastLatencyMs > 0, "expected a latency measurement to be recorded");

        await host.DisposeAsync();
    }
}
