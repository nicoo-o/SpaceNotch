using System;

namespace SpaceNotch.Core.Events;

public interface IEventBus
{
    void Publish<TEvent>(TEvent @event);
    IDisposable Subscribe<TEvent>(Action<TEvent> handler);
}
