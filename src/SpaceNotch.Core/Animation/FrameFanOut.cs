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

    /// <summary>
    /// Rappel d'échec de chaque inscription, de même rang que <see cref="_handlers"/>
    /// (null : aucun). Lu seulement à l'ajout, au retrait et à l'échec, jamais par image.
    /// </summary>
    private readonly List<Action?> _failureCallbacks = [];

    /// <summary>
    /// Copie de la liste pour l'image, refaite seulement quand la liste change :
    /// une copie neuve à chaque image allouait sur le chemin le plus chaud (n° 46).
    /// </summary>
    private EventHandler<object>[] _snapshot = [];
    private bool _snapshotStale = true;
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
    /// <param name="handler">L'abonné, appelé une fois par image.</param>
    /// <param name="onFailed">
    /// Appelé, l'abonné déjà retiré, quand il lève une erreur. Un propriétaire qui
    /// garde un drapeau « je tourne » y remet le drapeau à faux : sans cela, son
    /// démarrage suivant sortait tôt et ne se réabonnait jamais. Pas appelé quand
    /// l'abonné est retiré par <see cref="Remove"/>.
    /// </param>
    public void Add(EventHandler<object>? handler, Action? onFailed = null)
    {
        if (handler is null)
        {
            return;
        }

        _handlers.Add(handler);
        _failureCallbacks.Add(onFailed);
        _snapshotStale = true;

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
                _failureCallbacks.RemoveAt(_failureCallbacks.Count - 1);
                _snapshotStale = true;
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
            _failureCallbacks.RemoveAt(index);
            _snapshotStale = true;
        }

        if (_handlers.Count == 0 && _attached)
        {
            _attached = false;
            _detach();
        }
    }

    private void Fail(EventHandler<object> handler, Exception exception)
    {
        // Lu avant le retrait, qui défait l'inscription et son rang.
        int index = _handlers.LastIndexOf(handler);
        Action? onFailed = index >= 0 ? _failureCallbacks[index] : null;

        try
        {
            Remove(handler);
            _onError?.Invoke(exception);
        }
        finally
        {
            // Même si le retrait ou le journal échoue : un rappel sauté laisserait le
            // drapeau du propriétaire vrai, soit le défaut qu'il corrige.
            NotifyOwner(onFailed);
        }
    }

    private void NotifyOwner(Action? onFailed)
    {
        if (onFailed is null)
        {
            return;
        }

        // Le rappel d'un propriétaire ne doit pas priver les autres de leur image :
        // l'erreur qui sortirait d'ici traverserait Raise jusqu'au signal de rendu.
        try
        {
            onFailed();
        }
        catch (Exception ex)
        {
            // Enveloppée : le journal dit que c'est le rappel qui a échoué, pas une animation de plus.
            _onError?.Invoke(new InvalidOperationException("Le rappel d'échec d'un abonné de l'horloge a lui-même échoué.", ex));
        }
    }

    /// <summary>
    /// Une image : chaque abonné est appelé une fois. Un abonné retiré pendant
    /// l'image par un autre n'est plus appelé ; un abonné ajouté pendant
    /// l'image l'est à la suivante. Un abonné qui lève une erreur est retiré :
    /// il n'empêche plus les autres d'avancer, et son propriétaire en est prévenu
    /// (<c>onFailed</c> de <see cref="Add"/>) pour pouvoir se réabonner.
    /// </summary>
    public void Raise(object? sender, object args)
    {
        if (_handlers.Count == 0)
        {
            return;
        }

        if (_snapshotStale)
        {
            _snapshot = [.. _handlers];
            _snapshotStale = false;
        }

        EventHandler<object>[] snapshot = _snapshot;
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
