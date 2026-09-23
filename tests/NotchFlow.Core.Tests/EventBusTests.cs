using System;
using System.Collections.Generic;
using NotchFlow.Core.Events;
using Xunit;

namespace NotchFlow.Core.Tests;

public class EventBusTests
{
    private sealed record TestEvent(int Value);

    [Fact]
    public void EventBus_PublishesToAllSubscribers()
    {
        var bus = new EventBus();
        var received = new List<int>();

        bus.Subscribe<TestEvent>(e => received.Add(e.Value));
        bus.Subscribe<TestEvent>(e => received.Add(e.Value * 10));

        bus.Publish(new TestEvent(2));

        Assert.Equal([2, 20], received);
    }

    [Fact]
    public void EventBus_DisposeStopsDelivery()
    {
        var bus = new EventBus();
        int count = 0;

        IDisposable subscription = bus.Subscribe<TestEvent>(_ => count++);

        bus.Publish(new TestEvent(1));
        subscription.Dispose();
        bus.Publish(new TestEvent(2));

        Assert.Equal(1, count);
        Assert.Equal(0, bus.SubscriberCount<TestEvent>());
    }

    [Fact]
    public void EventBus_FailingSubscriberDoesNotInterruptOthers_AndIsReported()
    {
        var bus = new EventBus();
        var failures = new List<Type>();
        bool secondHandlerRan = false;

        bus.HandlerFailed = (eventType, _) => failures.Add(eventType);

        bus.Subscribe<TestEvent>(_ => throw new InvalidOperationException("panne simulée"));
        bus.Subscribe<TestEvent>(_ => secondHandlerRan = true);

        bus.Publish(new TestEvent(1));

        // Un abonné défaillant ne coupe pas la chaîne…
        Assert.True(secondHandlerRan);

        // …et sa défaillance n'est pas silencieuse pour autant.
        Assert.Single(failures);
        Assert.Equal(typeof(TestEvent), failures[0]);
    }

    [Fact]
    public void EventBus_IgnoresUnrelatedEventTypes()
    {
        var bus = new EventBus();
        bool received = false;

        bus.Subscribe<TestEvent>(_ => received = true);
        bus.Publish(new object());

        Assert.False(received);
    }
}
