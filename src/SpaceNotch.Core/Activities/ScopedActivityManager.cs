using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Vue restreinte du gestionnaire d'activités, donnée à un greffon.
///
/// <para>
/// Avant (audit SN-19), chaque greffon recevait le gestionnaire de l'application :
/// il pouvait effacer les activités des autres (<c>RemoveActivitiesFrom("media")</c>),
/// lire le contenu des notifications, épingler ou faire défiler la présentation.
/// </para>
/// <para>
/// Ici, un greffon ne voit, ne remplace et ne retire que les activités qu'il a
/// lui-même publiées. Il ne peut ni piloter la présentation ni forcer
/// l'expiration : ce sont des décisions de l'utilisateur et de l'hôte.
/// </para>
/// </summary>
public sealed class ScopedActivityManager : IActivityManager
{
    private readonly IActivityManager _inner;
    private readonly HashSet<string> _owned = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public ScopedActivityManager(IActivityManager inner)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));

        _inner.ActiveActivityChanged += (_, activity) =>
            ActiveActivityChanged?.Invoke(this, Owns(activity) ? activity : null);

        _inner.ActivityRemoved += (_, activity) =>
        {
            bool mine;

            lock (_lock)
            {
                mine = _owned.Remove(activity.Id);
            }

            if (mine)
            {
                ActivityRemoved?.Invoke(this, activity);
            }
        };
    }

    public IslandActivity? CurrentActivity
    {
        get
        {
            IslandActivity? current = _inner.CurrentActivity;
            return Owns(current) ? current : null;
        }
    }

    public int Count
    {
        get
        {
            lock (_lock)
            {
                return _owned.Count;
            }
        }
    }

    public event EventHandler<IslandActivity?>? ActiveActivityChanged;

    public event EventHandler<IslandActivity>? ActivityRemoved;

    public void PostActivity(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        lock (_lock)
        {
            // Un identifiant déjà tenu par une autre source n'est pas remplaçable :
            // sans cela, republier « media » écraserait le lecteur de l'hôte.
            // La publication est ignorée plutôt que levée : une exception partie
            // d'un greffon ne doit pas atteindre le fil d'interface.
            if (!_owned.Contains(activity.Id)
                && _inner.GetActiveActivities().Any(a => a.Id == activity.Id))
            {
                return;
            }

            _owned.Add(activity.Id);
        }

        _inner.PostActivity(activity);
    }

    public bool RemoveActivity(string activityId)
        => Owns(activityId) && _inner.RemoveActivity(activityId);

    public int RemoveActivitiesFrom(string featureId)
    {
        int removed = 0;

        foreach (IslandActivity activity in GetActiveActivities())
        {
            if (activity.FeatureId == featureId && _inner.RemoveActivity(activity.Id))
            {
                removed++;
            }
        }

        return removed;
    }

    public IReadOnlyList<IslandActivity> GetActiveActivities()
        => _inner.GetActiveActivities().Where(Owns).ToList();

    /// <summary>Sans effet : épingler revient à l'utilisateur.</summary>
    public void PinPresentation(string? activityId)
    {
    }

    /// <summary>Sans effet : parcourir la pile revient à l'utilisateur.</summary>
    public bool CyclePresentation(int delta) => false;

    /// <summary>Sans effet : l'hôte fait expirer les activités.</summary>
    public int ExpireOverdue(DateTimeOffset now, string? spare = null) => 0;

    public TimeSpan? GetTimeUntilNextExpiration(DateTimeOffset now, string? spare = null) => null;

    private bool Owns(IslandActivity? activity) => activity is not null && Owns(activity.Id);

    private bool Owns(string activityId)
    {
        lock (_lock)
        {
            return _owned.Contains(activityId);
        }
    }
}
