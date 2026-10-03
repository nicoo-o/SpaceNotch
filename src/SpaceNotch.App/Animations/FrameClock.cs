using System;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Animations;

/// <summary>
/// L'horloge d'images de l'application (phase D) : remplace les abonnements
/// directs à <see cref="CompositionTarget.Rendering"/>. Même forme
/// d'événement, donc même usage ; un seul abonnement réel, posé seulement
/// quand quelque chose bouge.
///
/// <para>
/// Fil d'interface uniquement : un abonnement venu d'un autre fil y est
/// renvoyé (v1.16.1). Fait sur place, il échouait et figeait toutes les
/// animations — la notch restait vide, sans yeux, jusqu'au redémarrage.
/// </para>
/// </summary>
public static class FrameClock
{
    private static readonly FrameFanOut Fan = new(
        () => CompositionTarget.Rendering += OnRendering,
        () => CompositionTarget.Rendering -= OnRendering,
        ex => MiniLogger.Log("[ANIMATION] une animation a échoué ; elle est arrêtée, les autres continuent", ex));

    private static DispatcherQueue? _queue;

    /// <summary>Retient le fil d'interface. Appelé une fois, depuis ce fil.</summary>
    public static void Attach(DispatcherQueue queue) => _queue ??= queue;

    /// <summary>Une image va être dessinée.</summary>
    public static event EventHandler<object> Rendering
    {
        add => OnUiThread(() => Fan.Add(value));
        remove => OnUiThread(() => Fan.Remove(value));
    }

    /// <summary>Nombre d'animations abonnées (diagnostics).</summary>
    public static int Subscribers => Fan.Count;

    private static void OnUiThread(Action action)
    {
        if (_queue is { HasThreadAccess: false } queue)
        {
            queue.TryEnqueue(() => action());
            return;
        }

        action();
    }

    private static void OnRendering(object? sender, object e) => Fan.Raise(sender, e);
}
