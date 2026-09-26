using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class ActivityManagerTests
{
    private static IslandActivity Activity(
        string id,
        ActivityPriority priority,
        string featureId = "feature.test",
        string sceneKey = IslandSceneCatalog.Pill,
        TimeSpan? duration = null,
        DateTimeOffset? createdAt = null)
        => new()
        {
            Id = id,
            FeatureId = featureId,
            SceneKey = sceneKey,
            Title = id,
            Priority = priority,
            Duration = duration,
            CreatedAt = createdAt ?? DateTimeOffset.UtcNow
        };

    [Fact]
    public void ActivityManager_ArbitratesByPriority()
    {
        var manager = new ActivityManager();

        manager.PostActivity(Activity("music", ActivityPriority.Background));
        Assert.Equal("music", manager.CurrentActivity?.Id);

        // Une activité plus prioritaire prend la main.
        manager.PostActivity(Activity("battery", ActivityPriority.High));
        Assert.Equal("battery", manager.CurrentActivity?.Id);

        // Quand elle se termine, la musique reprend sa place.
        manager.RemoveActivity("battery");
        Assert.Equal("music", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void A_more_important_activity_lifts_the_pin()
    {
        var manager = new ActivityManager();

        manager.PostActivity(Activity("music", ActivityPriority.Background));
        manager.PostActivity(Activity("clipboard", ActivityPriority.Normal));
        manager.PinPresentation("music");
        Assert.Equal("music", manager.CurrentActivity?.Id);

        // Une activité de même rang n'enlève pas l'épingle…
        manager.PostActivity(Activity("shelf", ActivityPriority.Normal));
        Assert.Equal("music", manager.CurrentActivity?.Id);

        // … mais le volume, plus important, reprend la main.
        manager.PostActivity(Activity("volume", ActivityPriority.High));
        Assert.Equal("volume", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Republishing_the_pinned_activity_keeps_the_pin()
    {
        var manager = new ActivityManager();

        manager.PostActivity(Activity("music", ActivityPriority.Background));
        manager.PostActivity(Activity("download", ActivityPriority.Normal));
        manager.PinPresentation("music");

        manager.PostActivity(Activity("music", ActivityPriority.Background));
        Assert.Equal("music", manager.CurrentActivity?.Id);
    }

    [Fact]
    public void Cycling_updates_the_current_activity()
    {
        var manager = new ActivityManager();

        manager.PostActivity(Activity("a", ActivityPriority.Normal, createdAt: DateTimeOffset.UtcNow.AddSeconds(-2)));
        manager.PostActivity(Activity("b", ActivityPriority.Normal, createdAt: DateTimeOffset.UtcNow.AddSeconds(-1)));
        string? before = manager.CurrentActivity?.Id;

        Assert.True(manager.CyclePresentation(1));
        Assert.NotEqual(before, manager.CurrentActivity?.Id);
    }

    [Fact]
    public void ActivityManager_RepostingSameId_ReplacesInsteadOfAccumulating()
    {
        // C'est la garantie anti-fuite : cinquante changements de volume ne
        // doivent laisser qu'une seule activité derrière eux.
        var manager = new ActivityManager();

        for (int i = 0; i < 50; i++)
        {
            manager.PostActivity(Activity("feature.hud.volume", ActivityPriority.High));
        }

        Assert.Equal(1, manager.Count);
    }

    [Fact]
    public void ActivityManager_ExpiresActivitiesWithDuration()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var manager = new ActivityManager(() => now);

        manager.PostActivity(Activity(
            "notif",
            ActivityPriority.High,
            duration: TimeSpan.FromSeconds(2),
            createdAt: now));

        Assert.Equal(1, manager.Count);

        // Pas encore échue.
        Assert.Equal(0, manager.ExpireOverdue(now));

        // Échue : retirée automatiquement, sans que la fonctionnalité ait à s'en
        // occuper.
        Assert.Equal(1, manager.ExpireOverdue(now.AddSeconds(3)));
        Assert.Equal(0, manager.Count);
        Assert.Null(manager.CurrentActivity);
    }

    [Fact]
    public void ActivityManager_CapsBackgroundActivities()
    {
        // Le plafond borne la mémoire de façon déterministe, indépendamment du
        // nombre d'événements reçus.
        var manager = new ActivityManager(() => DateTimeOffset.UtcNow, maxBackgroundActivities: 3);

        for (int i = 0; i < 10; i++)
        {
            manager.PostActivity(Activity($"bg.{i}", ActivityPriority.Background));
        }

        Assert.Equal(3, manager.Count);
    }

    [Fact]
    public void ActivityManager_ReportsNextExpirationDelay()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var manager = new ActivityManager(() => now);

        // Sans activité temporaire, l'hôte peut désarmer son minuteur.
        Assert.Null(manager.GetTimeUntilNextExpiration(now));

        manager.PostActivity(Activity("a", ActivityPriority.Normal, duration: TimeSpan.FromSeconds(30), createdAt: now));
        manager.PostActivity(Activity("b", ActivityPriority.Normal, duration: TimeSpan.FromSeconds(10), createdAt: now));

        TimeSpan? delay = manager.GetTimeUntilNextExpiration(now);

        Assert.NotNull(delay);
        Assert.True(delay!.Value <= TimeSpan.FromSeconds(10));
    }

    [Fact]
    public void ActivityManager_RemovesActivitiesFromFeature()
    {
        var manager = new ActivityManager();

        manager.PostActivity(Activity("a", ActivityPriority.Normal, featureId: "feature.one"));
        manager.PostActivity(Activity("b", ActivityPriority.Normal, featureId: "feature.two"));

        int removed = manager.RemoveActivitiesFrom("feature.one");

        Assert.Equal(1, removed);
        Assert.Equal("b", manager.CurrentActivity?.Id);
        Assert.Equal("feature.two", manager.CurrentActivity?.FeatureId);
    }

    [Fact]
    public void ActivityManager_RejectsActivityWithoutScene()
    {
        var manager = new ActivityManager();

        var invalid = new IslandActivity
        {
            Id = "x",
            FeatureId = "feature.test",
            SceneKey = "   ",
            Title = "sans scène"
        };

        Assert.Throws<ArgumentException>(() => manager.PostActivity(invalid));
    }
}
