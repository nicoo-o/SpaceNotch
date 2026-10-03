using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Animation;

/// <summary>
/// Horloge d'images unique (phase D, RFC §3.5) : un seul abonnement au signal
/// d'image du système, redistribué à tous ceux qui animent. Il n'est posé que
/// tant qu'au moins un abonné existe, et retiré dès que le dernier part :
/// rien ne tourne au repos.
///
/// <para>
/// Avant, chaque animation (ressort de la forme, détachement, visage,
/// passages, teinte, compteurs) s'abonnait elle-même, et quelques minuteurs à
/// 16 ms doublaient le tout : jusqu'à six réveils par image pour une seule
/// image à dessiner.
/// </para>
/// </summary>
public sealed class FrameFanOut
{
    private readonly List<EventHandler<object>> _handlers = [];
    private readonly Action _attach;
    private readonly Action _detach;
    private bool _attached;

    /// <param name="attach">Pose l'abonnement unique au signal d'image.</param>
    /// <param name="detach">Le retire.</param>
    public FrameFanOut(Action attach, Action detach)
    {
        _attach = attach ?? throw new ArgumentNullException(nameof(attach));
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
    }

    /// <summary>Nombre d'abonnés.</summary>
    public int Count => _handlers.Count;

    /// <summary>Vrai tant que l'abonnement unique est posé.</summary>
    public bool IsAttached => _attached;

    /// <summary>Ajoute un abonné ; le premier pose l'abonnement unique.</summary>
    public void Add(EventHandler<object>? handler)
    {
        if (handler is null)
        {
            return;
        }

        _handlers.Add(handler);

        if (!_attached)
        {
            _attached = true;
            _attach();
        }
    }

    /// <summary>Retire un abonné (sa dernière inscription) ; le dernier retire l'abonnement unique.</summary>
    public void Remove(EventHandler<object>? handler)
    {
        if (handler is null)
        {
            return;
        }

        int index = _handlers.LastIndexOf(handler);

        if (index >= 0)
        {
            _handlers.RemoveAt(index);
        }

        if (_handlers.Count == 0 && _attached)
        {
            _attached = false;
            _detach();
        }
    }

    /// <summary>
    /// Une image : chaque abonné est appelé une fois. Un abonné retiré pendant
    /// l'image par un autre n'est plus appelé ; un abonné ajouté pendant
    /// l'image l'est à la suivante.
    /// </summary>
    public void Raise(object? sender, object args)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        EventHandler<object>[] snapshot = [.. _handlers];

        foreach (EventHandler<object> handler in snapshot)
        {
            if (_handlers.Contains(handler))
            {
                handler(sender, args);
            }
        }
    }
}
