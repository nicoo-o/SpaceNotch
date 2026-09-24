using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Notifications;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Notifications groupées : « Discord 4 » plutôt que quatre cartes qui se chassent.
/// </summary>
public class NotificationGroupsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OneAppIsOneActivity_WhoseCountGrows()
    {
        var groups = new NotificationGroups();

        IslandActivity first = groups.Add("f", "Discord", "Lucas", "Salut", T0);
        IslandActivity fourth = groups.Add("f", "Discord", "Alex", "On y va ?", T0.AddSeconds(3));
        groups.Add("f", "Discord", "Marie", "Ok", T0.AddSeconds(1));
        fourth = groups.Add("f", "Discord", "Alex", "On y va ?", T0.AddSeconds(3));

        Assert.Equal(first.Id, fourth.Id);
        Assert.Null(first.TrailingMetric);
        Assert.Equal("4", fourth.TrailingMetric);
        Assert.Equal("Alex", fourth.Title);
        Assert.Equal("Discord", fourth.Eyebrow);

        var payload = Assert.IsType<NotificationGroupPayload>(fourth.Payload);
        Assert.Equal(4, payload.Count);
        Assert.Equal("Alex", payload.Items[0].Title);
    }

    [Fact]
    public void DifferentApps_AreDifferentGroups()
    {
        var groups = new NotificationGroups();

        Assert.NotEqual(
            groups.Add("f", "Discord", "a", "b", T0).Id,
            groups.Add("f", "Outlook", "a", "b", T0).Id);
    }

    [Fact]
    public void OldNotifications_LeaveTheGroup()
    {
        var groups = new NotificationGroups();

        groups.Add("f", "Discord", "old", "x", T0);
        IslandActivity fresh = groups.Add("f", "Discord", "new", "y", T0 + NotificationGroups.Window + TimeSpan.FromSeconds(1));

        Assert.Null(fresh.TrailingMetric);
    }

    [Fact]
    public void TheOpenedGroup_GrowsWithItsHistory_UpToThreeLines()
    {
        var groups = new NotificationGroups();
        double scene = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Notification).Height;

        IslandActivity one = groups.Add("f", "Discord", "a", "b", T0);
        Assert.Equal(scene, one.Footprint.Height);

        IslandActivity many = one;

        for (int i = 0; i < 8; i++)
        {
            many = groups.Add("f", "Discord", $"m{i}", "b", T0.AddSeconds(i + 1));
        }

        Assert.Equal(scene + (NotificationGroups.VisibleHistory * 20), many.Footprint.Height);
    }

    [Fact]
    public void ANotification_OverlaysWithoutOpening()
    {
        IslandActivity activity = new NotificationGroups().Add("f", "Discord", "a", "b", T0);

        Assert.Equal(ActivityPresentationPolicy.Temporary, ActivityPolicies.Resolve(activity));
        Assert.True(activity.Priority < ActivityPriority.High);
        Assert.Equal(IslandPresentationTier.Card, IslandPresentation.Resolve(activity));
    }
}
