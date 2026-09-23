using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace NotchFlow.Core.Events;

/// <summary>
/// Bus d'événements interne, purement événementiel : rien ne s'exécute tant
/// qu'aucun événement n'est publié.
///
/// Un abonné défaillant n'interrompt jamais la chaîne de diffusion, mais sa
/// défaillance n'est pas pour autant invisible : elle est signalée via
/// <see cref="HandlerFailed"/>, ce qui évite les pannes silencieuses.
/// </summary>
public sealed class EventBus : IEventBus
{
    private readonly ConcurrentDictionary<Type, List<object>> _subscribers = new();
    private readonly object _lock = new();

    /// <summary>
    /// Signalé lorsqu'un abonné lève une exception pendant la diffusion.
    /// Renseigné par la couche hôte pour journaliser.
    /// </summary>
    public Action<Type, Exception>? HandlerFailed { get; set; }

    public void Publish<TEvent>(TEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (!_subscribers.TryGetValue(typeof(TEvent), out var handlers))
        {
            return;
        }

        // Copie défensive : un abonné peut se désabonner pendant la diffusion.
        object[] snapshot;
        lock (_lock)
        {
            snapshot = handlers.ToArray();
        }

        foreach (object handler in snapshot)
        {
            if (handler is not Action<TEvent> action)
            {
                continue;
            }

            try
            {
                action(@event);
            }
            catch (Exception ex)
            {
                HandlerFailed?.Invoke(typeof(TEvent), ex);
            }
        }
    }

    public IDisposable Subscribe<TEvent>(Action<TEvent> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);

        var handlers = _subscribers.GetOrAdd(typeof(TEvent), _ => []);

        lock (_lock)
        {
            handlers.Add(handler);
        }

        return new Subscription(() =>
        {
            lock (_lock)
            {
                handlers.Remove(handler);
            }
        });
    }

    /// <summary>Nombre d'abonnés à un type d'événement donné.</summary>
    public int SubscriberCount<TEvent>()
    {
        if (!_subscribers.TryGetValue(typeof(TEvent), out var handlers))
        {
            return 0;
        }

        lock (_lock)
        {
            return handlers.Count;
        }
    }

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            Action? unsubscribe = _unsubscribe;
            _unsubscribe = null;
            unsubscribe?.Invoke();
        }
    }
}
