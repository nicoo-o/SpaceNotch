using System;
using System.Collections.Generic;
using System.Linq;

namespace NotchFlow.Core.Activities;

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
            next = EvaluateTop();
            _currentActivity = next;
        }

        if (!ReferenceEquals(previous, next))
        {
            ActiveActivityChanged?.Invoke(this, next);
        }
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

            if (ordered.Count < 2)
            {
                return false;
            }

            int index = ordered.FindIndex(
                a => string.Equals(a.Id, _currentActivity?.Id, StringComparison.Ordinal));

            if (index < 0)
            {
                index = 0;
            }

            int count = ordered.Count;
            int target = (((index + delta) % count) + count) % count;

            next = ordered[target];
            _pinnedActivityId = next.Id;
        }

        ActiveActivityChanged?.Invoke(this, next);
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

            EvictOverflowBackground(activity.Id, ref evicted);

            next = EvaluateTop();
            _currentActivity = next;
        }

        if (evicted is not null)
        {
            foreach (IslandActivity removed in evicted)
            {
                ActivityRemoved?.Invoke(this, removed);
            }
        }

        // On notifie aussi lorsqu'une activité déjà en tête est republiée : son
        // contenu a changé, l'affichage doit suivre.
        if (!ReferenceEquals(previous, next) || ReferenceEquals(next, activity))
        {
            ActiveActivityChanged?.Invoke(this, next);
        }
    }

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
        }

        ActivityRemoved?.Invoke(this, removed);

        if (!ReferenceEquals(previous, next))
        {
            ActiveActivityChanged?.Invoke(this, next);
        }

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
        }

        foreach (IslandActivity activity in removed)
        {
            ActivityRemoved?.Invoke(this, activity);
        }

        if (!ReferenceEquals(previous, next))
        {
            ActiveActivityChanged?.Invoke(this, next);
        }

        return removed.Count;
    }

    public IReadOnlyList<IslandActivity> GetActiveActivities()
    {
        lock (_lock)
        {
            return Order(_activities.Values).ToList();
        }
    }

    public int ExpireOverdue(DateTimeOffset now)
    {
        List<IslandActivity> expired;
        IslandActivity? previous;
        IslandActivity? next;

        lock (_lock)
        {
            expired = _activities.Values.Where(a => a.IsExpiredAt(now)).ToList();

            if (expired.Count == 0)
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
        }

        foreach (IslandActivity activity in expired)
        {
            ActivityRemoved?.Invoke(this, activity);
        }

        if (!ReferenceEquals(previous, next))
        {
            ActiveActivityChanged?.Invoke(this, next);
        }

        return expired.Count;
    }

    public TimeSpan? GetTimeUntilNextExpiration(DateTimeOffset now)
    {
        lock (_lock)
        {
            DateTimeOffset? nearest = _activities.Values
                .Select(a => a.ExpiresAt)
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
        }

        foreach (IslandActivity activity in removed)
        {
            ActivityRemoved?.Invoke(this, activity);
        }

        if (hadCurrent)
        {
            ActiveActivityChanged?.Invoke(this, null);
        }
    }

    /// <summary>
    /// Déclenché pour chaque activité retirée, quelle qu'en soit la raison
    /// (retrait explicite, expiration ou éviction).
    /// </summary>
    public event EventHandler<IslandActivity>? ActivityRemoved;

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
        }

        return Order(_activities.Values).FirstOrDefault();
    }

    private static IEnumerable<IslandActivity> Order(IEnumerable<IslandActivity> source)
        => source
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.CreatedAt);
}
