using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Arbitre central des activités et propriétaire de leur cycle de vie.
///
/// Trois garanties qu'il assure à lui seul :
/// <list type="bullet">
/// <item>republier une activité sous le même identifiant la <em>remplace</em> au
/// lieu de l'empiler — sans quoi chaque changement de volume laisserait une
/// entrée derrière lui ;</item>
/// <item>une activité dotée d'une durée de vie est retirée automatiquement, et
/// l'hôte peut connaître la prochaine échéance pour n'armer qu'un seul
/// minuteur borné ;</item>
/// <item>le nombre d'activités de fond est plafonné, ce qui borne la mémoire de
/// façon déterministe.</item>
/// </list>
/// </summary>
public sealed class ActivityManager : IActivityManager
{
    /// <summary>
    /// Nombre maximal d'activités d'arrière-plan conservées simultanément.
    /// Au-delà, la plus ancienne est évincée.
    /// </summary>
    public const int DefaultMaxBackgroundActivities = 8;

    private readonly object _lock = new();
    private readonly Dictionary<string, IslandActivity> _activities = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;
    private readonly int _maxBackgroundActivities;

    private IslandActivity? _currentActivity;

    /// <summary>
    /// Activité que l'utilisateur a explicitement mise en avant en parcourant la
    /// pile. Tant qu'elle existe, elle prime sur l'arbitrage par priorité : un
    /// choix délibéré ne doit pas être renversé par l'arrivée d'une notification.
    /// </summary>
    private string? _pinnedActivityId;

    /// <summary>
    /// Échéance de l'épingle posée par la molette sur une entrée de la pile
    /// (<see cref="Presentation.ActivityPresentationPolicy.Listed"/>) : une copie
    /// qu'on vient regarder rend la main d'elle-même, sans minuteur propre.
    /// </summary>
    private DateTimeOffset? _pinnedUntil;

    /// <summary>Temps pendant lequel une entrée de la pile, montrée par la molette, reste présentée.</summary>
    public static readonly TimeSpan ListedPinLease = TimeSpan.FromSeconds(8);

    public ActivityManager()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    /// <summary>
    /// Constructeur testable : l'horloge est injectable afin de vérifier
    /// l'expiration sans dépendre du temps réel.
    /// </summary>
    public ActivityManager(Func<DateTimeOffset> clock, int maxBackgroundActivities = DefaultMaxBackgroundActivities)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _maxBackgroundActivities = Math.Max(1, maxBackgroundActivities);
    }

    public IslandActivity? CurrentActivity
    {
        get
        {
            lock (_lock)
            {
                return _currentActivity;
            }
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _activities.Count;
            }
        }
    }

    public event EventHandler<IslandActivity?>? ActiveActivityChanged;

    /// <summary>
    /// Épingle l'activité présentée, ou <c>null</c> pour rendre la main à
    /// l'arbitrage automatique par priorité.
    /// </summary>
    public void PinPresentation(string? activityId)
    {
        IslandActivity? previous;
        IslandActivity? next;

        lock (_lock)
        {
            previous = _currentActivity;
            _pinnedActivityId = activityId;
            _pinnedUntil = null;
            next = EvaluateTop();
            _currentActivity = next;

            if (!ReferenceEquals(previous, next))
            {
                EnqueueChanged(next);
            }
        }

        Deliver();
    }

    /// <summary>
    /// Parcourt la pile : passe à l'activité suivante (<paramref name="delta"/> à
    /// 1) ou précédente (à −1), en boucle. L'activité retenue est épinglée.
    /// </summary>
    /// <returns><c>false</c> si la pile ne contient pas de quoi parcourir.</returns>
    public bool CyclePresentation(int delta)
    {
        IslandActivity? next;

        lock (_lock)
        {
            List<IslandActivity> ordered = Order(_activities.Values).ToList();
            int count = ordered.Count;

            // Depuis le repos, une seule entrée de la pile suffit à parcourir ;
            // depuis une activité présentée, il faut une autre activité.
            if (count == 0 || (_currentActivity is not null && count < 2))
            {
                return false;
            }

            int index = ordered.FindIndex(
                a => string.Equals(a.Id, _currentActivity?.Id, StringComparison.Ordinal));

            // Au repos, la molette commence par la première (ou la dernière)
            // entrée, au lieu de sauter d'un cran comme depuis une activité.
            int target = index < 0
                ? (delta > 0 ? 0 : count - 1)
                : (((index + delta) % count) + count) % count;

            next = ordered[target];
            _pinnedActivityId = next.Id;
            _pinnedUntil = Presentation.ActivityPolicies.Resolve(next) == Presentation.ActivityPresentationPolicy.Listed
                ? _clock() + ListedPinLease
                : null;
            _currentActivity = next;
            EnqueueChanged(next);
        }

        Deliver();
        return true;
    }

    public void PostActivity(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        Validate(activity);

        IslandActivity? previous;
        IslandActivity? next;
        List<IslandActivity>? evicted = null;

        lock (_lock)
        {
            previous = _currentActivity;

            // Remplacement par identifiant : le cœur de la garantie anti-fuite.
            _activities[activity.Id] = activity;

            // Une activité importante, plus prioritaire que celle qu'on a
            // épinglée, reprend la main : l'épingle choisit parmi ce qui attend,
            // elle ne fait pas taire le volume, un appel ou la fin d'un
            // minuteur. Une activité ordinaire — une copie, un fichier déposé —
            // attend son tour.
            if (_pinnedActivityId is not null
                && !string.Equals(_pinnedActivityId, activity.Id, StringComparison.Ordinal)
                && _activities.TryGetValue(_pinnedActivityId, out IslandActivity? pinned)
                && activity.Priority >= ActivityPriority.High
                && activity.Priority > pinned.Priority)
            {
                _pinnedActivityId = null;
            }

            EvictOverflowBackground(activity.Id, ref evicted);

            next = EvaluateTop();
            _currentActivity = next;

            if (evicted is not null)
            {
                foreach (IslandActivity removed in evicted)
                {
                    EnqueueRemoved(removed);
                }
            }

            // On notifie aussi lorsqu'une activité déjà en tête est republiée : son
            // contenu a changé, l'affichage doit suivre.
            if (!ReferenceEquals(previous, next) || ReferenceEquals(next, activity))
            {
                EnqueueChanged(next);
            }

            _outbox.Enqueue(() => ActivityPosted?.Invoke(this, activity));
        }

        Deliver();
    }

    /// <summary>
    /// Déclenché pour chaque publication, qu'elle change ou non l'activité en
    /// tête. Une activité importante publiée derrière celle qui est présentée
    /// ne change rien à la tête, mais elle fait naître une bulle.
    /// </summary>
    public event EventHandler<IslandActivity>? ActivityPosted;

    public bool RemoveActivity(string activityId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activityId);

        IslandActivity? removed;
        IslandActivity? previous;
        IslandActivity? next;

        lock (_lock)
        {
            if (!_activities.Remove(activityId, out removed))
            {
                return false;
            }

            previous = _currentActivity;
            next = EvaluateTop();
            _currentActivity = next;

            EnqueueRemoved(removed);

            if (!ReferenceEquals(previous, next))
            {
                EnqueueChanged(next);
            }
        }

        Deliver();
        return true;
    }

    public int RemoveActivitiesFrom(string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        List<IslandActivity> removed;
        IslandActivity? previous;
        IslandActivity? next;

        lock (_lock)
        {
            removed = _activities.Values
                .Where(a => string.Equals(a.FeatureId, featureId, StringComparison.Ordinal))
                .ToList();

            if (removed.Count == 0)
            {
                return 0;
            }

            foreach (IslandActivity activity in removed)
            {
                _activities.Remove(activity.Id);
            }

            previous = _currentActivity;
            next = EvaluateTop();
            _currentActivity = next;

            foreach (IslandActivity activity in removed)
            {
                EnqueueRemoved(activity);
            }

            if (!ReferenceEquals(previous, next))
            {
                EnqueueChanged(next);
            }
        }

        Deliver();
        return removed.Count;
    }

    public IReadOnlyList<IslandActivity> GetActiveActivities()
    {
        lock (_lock)
        {
            return Order(_activities.Values).ToList();
        }
    }

    public int ExpireOverdue(DateTimeOffset now, string? spare = null)
    {
        List<IslandActivity> expired;
        IslandActivity? previous;
        IslandActivity? next;

        lock (_lock)
        {
            expired = _activities.Values
                .Where(a => a.IsExpiredAt(now) && !string.Equals(a.Id, spare, StringComparison.Ordinal))
                .ToList();

            // Le bail d'une entrée de la pile montrée par la molette : échu, la
            // notch rend la main — sauf si l'utilisateur la regarde (ouverte,
            // survolée), comme pour l'expiration ordinaire.
            bool leaseOver = _pinnedUntil is { } until
                && until <= now
                && !string.Equals(_pinnedActivityId, spare, StringComparison.Ordinal);

            if (leaseOver)
            {
                _pinnedActivityId = null;
                _pinnedUntil = null;
            }

            if (expired.Count == 0 && !leaseOver)
            {
                return 0;
            }

            foreach (IslandActivity activity in expired)
            {
                _activities.Remove(activity.Id);
            }

            previous = _currentActivity;
            next = EvaluateTop();
            _currentActivity = next;

            foreach (IslandActivity activity in expired)
            {
                EnqueueRemoved(activity);
            }

            if (!ReferenceEquals(previous, next))
            {
                EnqueueChanged(next);
            }
        }

        Deliver();
        return expired.Count;
    }

    public TimeSpan? GetTimeUntilNextExpiration(DateTimeOffset now, string? spare = null)
    {
        lock (_lock)
        {
            DateTimeOffset? nearest = _activities.Values
                .Where(a => !string.Equals(a.Id, spare, StringComparison.Ordinal))
                .Select(a => a.ExpiresAt)
                .Append(string.Equals(_pinnedActivityId, spare, StringComparison.Ordinal) ? null : _pinnedUntil)
                .Where(e => e.HasValue)
                .Select(e => e!.Value)
                .DefaultIfEmpty()
                .Min();

            if (!nearest.HasValue || nearest.Value == default)
            {
                return null;
            }

            TimeSpan delay = nearest.Value - now;
            return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
        }
    }

    /// <summary>Retire toutes les activités — utilisé à l'arrêt de l'application.</summary>
    public void Clear()
    {
        IslandActivity[] removed;
        bool hadCurrent;

        lock (_lock)
        {
            removed = _activities.Values.ToArray();
            _activities.Clear();
            hadCurrent = _currentActivity is not null;
            _currentActivity = null;

            foreach (IslandActivity activity in removed)
            {
                EnqueueRemoved(activity);
            }

            if (hadCurrent)
            {
                EnqueueChanged(null);
            }
        }

        Deliver();
    }

    /// <summary>
    /// Déclenché pour chaque activité retirée, quelle qu'en soit la raison
    /// (retrait explicite, expiration ou éviction).
    /// </summary>
    public event EventHandler<IslandActivity>? ActivityRemoved;

    // ------------------------------------------------------------------
    // Livraison ordonnée (audit SN-21)
    // ------------------------------------------------------------------

    // Les événements sont mis en file sous le verrou, dans l'ordre exact des
    // changements d'état, puis livrés hors verrou par un seul livreur à la fois.
    // Avant, chaque appel les levait après avoir relâché le verrou : deux
    // publications concurrentes pouvaient annoncer leurs têtes dans le désordre,
    // et la notch afficher une activité périmée. Un abonné qui publie depuis son
    // gestionnaire voit aussi son événement livré après les autres abonnés.
    private readonly Queue<Action> _outbox = new();
    private bool _delivering;

    private void EnqueueChanged(IslandActivity? next)
        => _outbox.Enqueue(() => ActiveActivityChanged?.Invoke(this, next));

    private void EnqueueRemoved(IslandActivity removed)
        => _outbox.Enqueue(() => ActivityRemoved?.Invoke(this, removed));

    private void Deliver()
    {
        lock (_lock)
        {
            if (_delivering)
            {
                return;
            }

            _delivering = true;
        }

        while (true)
        {
            Action notify;

            lock (_lock)
            {
                if (!_outbox.TryDequeue(out notify!))
                {
                    _delivering = false;
                    return;
                }
            }

            try
            {
                notify();
            }
            catch
            {
                // Un abonné qui lève ne bloque pas la file : le suivant livrera.
                lock (_lock)
                {
                    _delivering = false;
                }

                throw;
            }
        }
    }

    private static void Validate(IslandActivity activity)
    {
        if (string.IsNullOrWhiteSpace(activity.Id))
        {
            throw new ArgumentException("Une activité doit porter un identifiant.", nameof(activity));
        }

        if (string.IsNullOrWhiteSpace(activity.FeatureId))
        {
            throw new ArgumentException("Une activité doit déclarer sa fonctionnalité.", nameof(activity));
        }

        if (string.IsNullOrWhiteSpace(activity.SceneKey))
        {
            throw new ArgumentException("Une activité doit déclarer sa scène.", nameof(activity));
        }
    }

    /// <summary>
    /// Évince les activités d'arrière-plan les plus anciennes lorsque le plafond
    /// est dépassé, en préservant l'activité qui vient d'être publiée.
    /// </summary>
    private void EvictOverflowBackground(string protectedId, ref List<IslandActivity>? evicted)
    {
        var background = _activities.Values
            .Where(a => a.Priority == ActivityPriority.Background)
            .ToList();

        while (background.Count > _maxBackgroundActivities)
        {
            IslandActivity oldest = background
                .Where(a => !string.Equals(a.Id, protectedId, StringComparison.Ordinal))
                .OrderBy(a => a.CreatedAt)
                .FirstOrDefault()!;

            if (oldest is null)
            {
                break;
            }

            _activities.Remove(oldest.Id);
            background.Remove(oldest);
            (evicted ??= []).Add(oldest);
        }
    }

    private IslandActivity? EvaluateTop()
    {
        if (_pinnedActivityId is not null)
        {
            if (_activities.TryGetValue(_pinnedActivityId, out IslandActivity? pinned))
            {
                return pinned;
            }

            // L'activité épinglée a disparu (expiration, retrait) : la présentation
            // revient d'elle-même à l'arbitrage automatique. Sans cette remise à
            // zéro, la pile resterait figée sur une référence morte.
            _pinnedActivityId = null;
            _pinnedUntil = null;
        }

        // Une entrée de la pile seulement n'est jamais présentée d'office.
        return Order(_activities.Values.Where(a => Presentation.ActivityPolicies.Resolve(a) != Presentation.ActivityPresentationPolicy.Listed)).FirstOrDefault();
    }

    private static IEnumerable<IslandActivity> Order(IEnumerable<IslandActivity> source)
        => source
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.CreatedAt);
}
