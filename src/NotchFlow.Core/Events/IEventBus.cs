using System;

namespace NotchFlow.Core.Events;

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event);
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}
