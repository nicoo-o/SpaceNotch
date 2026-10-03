using System;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;

namespace SpaceNotch_App.Animations;

/// <summary>
/// L'horloge d'images de l'application (phase D) : remplace les abonnements
/// directs à <see cref="CompositionTarget.Rendering"/>. Même forme
/// d'événement, donc même usage ; un seul abonnement réel, posé seulement
/// quand quelque chose bouge. Fil d'interface uniquement.
/// </summary>
public static class FrameClock
{
    private static readonly FrameFanOut Fan = new(
        () => CompositionTarget.Rendering += OnRendering,
        () => CompositionTarget.Rendering -= OnRendering);

    /// <summary>Une image va être dessinée.</summary>
    public static event EventHandler<object> Rendering
    {
        add => Fan.Add(value);
        remove => Fan.Remove(value);
    }

    /// <summary>Nombre d'animations abonnées (diagnostics).</summary>
    public static int Subscribers => Fan.Count;

    private static void OnRendering(object? sender, object e) => Fan.Raise(sender, e);
}
