using Centaur.Core.Hosting;
using Xunit;

namespace Centaur.Tests;

public class ExtensionHostTests
{
    interface ITestProvider : IProvider
    {
        string Name { get; }
    }

    class TestProvider(string name, int priority = 1000) : ITestProvider
    {
        public string Name => name;
        public int Priority => priority;
    }

    class TrackingExtension(string name) : IExtension
    {
        public static List<string> ActivationOrder { get; } = [];
        public static List<string> DisposalOrder { get; } = [];
        public bool Activated { get; private set; }
        public IExtensionContext? Context { get; private set; }

        public Task ActivateAsync(IExtensionContext context)
        {
            Activated = true;
            Context = context;
            ActivationOrder.Add(name);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            DisposalOrder.Add(name);
            return ValueTask.CompletedTask;
        }
    }

    class ExtensionThatIsAlsoProvider : IExtension, ITestProvider
    {
        public string Name => "dual";
        public int Priority => 500;

        public Task ActivateAsync(IExtensionContext context) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task ActivateAsync_CallsActivateOnAllExtensions()
    {
        var host = new ExtensionHost();
        var ext = new TrackingExtension("a");
        host.RegisterExtension(ext);

        await host.ActivateAsync();

        Assert.True(ext.Activated);
        Assert.NotNull(ext.Context);
    }

    [Fact]
    public async Task ActivateAsync_ActivatesInRegistrationOrder()
    {
        TrackingExtension.ActivationOrder.Clear();
        var host = new ExtensionHost();
        host.RegisterExtension(new TrackingExtension("first"));
        host.RegisterExtension(new TrackingExtension("second"));
        host.RegisterExtension(new TrackingExtension("third"));

        await host.ActivateAsync();

        Assert.Equal(["first", "second", "third"], TrackingExtension.ActivationOrder);
    }

    [Fact]
    public async Task DisposeAsync_DisposesInReverseOrder()
    {
        TrackingExtension.DisposalOrder.Clear();
        var host = new ExtensionHost();
        host.RegisterExtension(new TrackingExtension("first"));
        host.RegisterExtension(new TrackingExtension("second"));
        host.RegisterExtension(new TrackingExtension("third"));

        await host.ActivateAsync();
        await host.DisposeAsync();

        Assert.Equal(["third", "second", "first"], TrackingExtension.DisposalOrder);
    }

    [Fact]
    public void GetProvider_ReturnsRegisteredProvider()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new TestProvider("test"));

        var provider = host.GetProvider<ITestProvider>();

        Assert.NotNull(provider);
        Assert.Equal("test", provider.Name);
    }

    [Fact]
    public void GetProvider_ReturnsHighestPriorityFirst()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new TestProvider("low", priority: 1000));
        host.RegisterProvider(new TestProvider("high", priority: 100));

        var provider = host.GetProvider<ITestProvider>();

        Assert.NotNull(provider);
        Assert.Equal("high", provider.Name);
    }

    [Fact]
    public void GetProviders_ReturnsAllMatchingProviders()
    {
        var host = new ExtensionHost();
        host.RegisterProvider(new TestProvider("a"));
        host.RegisterProvider(new TestProvider("b"));

        var providers = host.GetProviders<ITestProvider>();

        Assert.Equal(2, providers.Count);
    }

    [Fact]
    public void GetProvider_ReturnsNullWhenNoneRegistered()
    {
        var host = new ExtensionHost();
        Assert.Null(host.GetProvider<ITestProvider>());
    }

    [Fact]
    public async Task RegisterExtension_ThatIsAlsoProvider_RegistersBoth()
    {
        var host = new ExtensionHost();
        var dual = new ExtensionThatIsAlsoProvider();
        host.RegisterExtension(dual);

        await host.ActivateAsync();

        var provider = host.GetProvider<ITestProvider>();
        Assert.NotNull(provider);
        Assert.Equal("dual", provider.Name);
    }

    [Fact]
    public async Task RegisterAfterActivation_Throws()
    {
        var host = new ExtensionHost();
        await host.ActivateAsync();

        Assert.Throws<InvalidOperationException>(() => host.RegisterProvider(new TestProvider("late")));
        Assert.Throws<InvalidOperationException>(() => host.RegisterExtension(new TrackingExtension("late")));
    }

    [Fact]
    public async Task ActivateAsync_PublishesTerminalReadyEvent()
    {
        var host = new ExtensionHost();
        bool readyReceived = false;
        host.Events.Subscribe<TerminalReadyEvent>(_ => readyReceived = true);

        await host.ActivateAsync();

        Assert.True(readyReceived);
    }

    [Fact]
    public async Task DisposeAsync_PublishesTerminalShutdownEvent()
    {
        var host = new ExtensionHost();
        bool shutdownReceived = false;
        host.Events.Subscribe<TerminalShutdownEvent>(_ => shutdownReceived = true);
        await host.ActivateAsync();

        await host.DisposeAsync();

        Assert.True(shutdownReceived);
    }
}
