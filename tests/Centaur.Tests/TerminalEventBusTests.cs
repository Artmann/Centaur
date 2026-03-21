using Centaur.Core.Hosting;
using Xunit;

namespace Centaur.Tests;

public class TerminalEventBusTests
{
    readonly TerminalEventBus bus = new();

    record TestEvent(string Message);
    record OtherEvent(int Value);

    [Fact]
    public void Publish_CallsSyncSubscriber()
    {
        string? received = null;
        bus.Subscribe<TestEvent>(e => received = e.Message);

        bus.Publish(new TestEvent("hello"));

        Assert.Equal("hello", received);
    }

    [Fact]
    public void Publish_CallsMultipleSubscribers()
    {
        var calls = new List<string>();
        bus.Subscribe<TestEvent>(e => calls.Add("first"));
        bus.Subscribe<TestEvent>(e => calls.Add("second"));

        bus.Publish(new TestEvent("test"));

        Assert.Equal(["first", "second"], calls);
    }

    [Fact]
    public void Publish_DoesNotCrossEventTypes()
    {
        bool called = false;
        bus.Subscribe<OtherEvent>(e => called = true);

        bus.Publish(new TestEvent("hello"));

        Assert.False(called);
    }

    [Fact]
    public void Publish_NoSubscribers_DoesNotThrow()
    {
        bus.Publish(new TestEvent("no one listening"));
    }

    [Fact]
    public void Unsubscribe_ViaDispose_StopsReceiving()
    {
        int callCount = 0;
        var sub = bus.Subscribe<TestEvent>(e => callCount++);

        bus.Publish(new TestEvent("first"));
        sub.Dispose();
        bus.Publish(new TestEvent("second"));

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task PublishAsync_CallsAsyncSubscriber()
    {
        string? received = null;
        bus.Subscribe<TestEvent>(async e =>
        {
            await Task.Delay(1);
            received = e.Message;
        });

        await bus.PublishAsync(new TestEvent("async hello"));

        Assert.Equal("async hello", received);
    }

    [Fact]
    public async Task PublishAsync_CallsBothSyncAndAsyncSubscribers()
    {
        var calls = new List<string>();
        bus.Subscribe<TestEvent>(e => calls.Add("sync"));
        bus.Subscribe<TestEvent>(async e =>
        {
            await Task.Delay(1);
            calls.Add("async");
        });

        await bus.PublishAsync(new TestEvent("test"));

        Assert.Equal(["sync", "async"], calls);
    }

    [Fact]
    public void SubscriberCanUnsubscribeDuringPublish()
    {
        IDisposable? sub = null;
        sub = bus.Subscribe<TestEvent>(e => sub!.Dispose());

        // Should not throw
        bus.Publish(new TestEvent("test"));
    }
}
