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
    private readonly Action<Exception>? _onError;
    private bool _attached;

    /// <param name="attach">Pose l'abonnement unique au signal d'image.</param>
    /// <param name="detach">Le retire.</param>
    /// <param name="onError">Reçoit l'erreur d'un abonné, qui est alors retiré.</param>
    public FrameFanOut(Action attach, Action detach, Action<Exception>? onError = null)
    {
        _attach = attach ?? throw new ArgumentNullException(nameof(attach));
        _detach = detach ?? throw new ArgumentNullException(nameof(detach));
        _onError = onError;
    }

    /// <summary>
    /// Mesure de fluidité : reçoit, pour chaque abonné, le temps qu'il a pris
    /// dans l'image et ce qu'il a alloué sur ce fil, en octets (les GC des
    /// animations viennent d'allocations par image, n° 46). Null par défaut, et
    /// alors rien n'est mesuré.
    /// </summary>
    public Action<EventHandler<object>, TimeSpan, long>? Timed { get; set; }

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
            // Branché seulement si le branchement a réussi (v1.16.1) : marqué
            // avant, un échec — un abonnement venu d'un autre fil — laissait
            // l'horloge se croire branchée sans jamais battre, et toutes les
            // animations de la notch restaient figées jusqu'au redémarrage.
            try
            {
                _attach();
            }
            catch
            {
                _handlers.RemoveAt(_handlers.Count - 1);
                throw;
            }

            _attached = true;
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

    private void Fail(EventHandler<object> handler, Exception exception)
    {
        Remove(handler);
        _onError?.Invoke(exception);
    }

    /// <summary>
    /// Une image : chaque abonné est appelé une fois. Un abonné retiré pendant
    /// l'image par un autre n'est plus appelé ; un abonné ajouté pendant
    /// l'image l'est à la suivante. Un abonné qui lève une erreur est retiré :
    /// il n'empêche plus les autres d'avancer.
    /// </summary>
    public void Raise(object? sender, object args)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        EventHandler<object>[] snapshot = [.. _handlers];
        Action<EventHandler<object>, TimeSpan, long>? timed = Timed;

        foreach (EventHandler<object> handler in snapshot)
        {
            if (_handlers.Contains(handler))
            {
                long started = timed is null ? 0 : System.Diagnostics.Stopwatch.GetTimestamp();
                long allocatedBefore = timed is null ? 0 : GC.GetAllocatedBytesForCurrentThread();

                try
                {
                    handler(sender, args);
                }
                catch (Exception ex)
                {
                    Fail(handler, ex);
                }

                timed?.Invoke(handler, System.Diagnostics.Stopwatch.GetElapsedTime(started), timed is null ? 0 : GC.GetAllocatedBytesForCurrentThread() - allocatedBefore);
            }
        }
    }
}
