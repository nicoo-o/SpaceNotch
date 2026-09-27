using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Features.Calendar;
using SpaceNotch.Features.Share;
using SpaceNotch.Platform.Windows.Calendar;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Rendez-vous (F2) et partage (F8), sans Windows.</summary>
public class Wave5cFeatureTests
{
    [Fact]
    public void AMeeting_CountsDown_ThenOffersToJoin()
    {
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2));
        DateTimeOffset now = start.AddMinutes(-3);
        var activities = new ActivityManager();
        var feature = new MeetingFeature(activities, new EventBus(), reader: null, now: () => now);
        var meeting = new CalendarMeeting("m1", "Point produit", start, start.AddMinutes(30), "Visio", "Lien : https://meet.google.com/abc-defg-hij", null);

        feature.Show(meeting);
        IslandActivity soon = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Point produit", soon.Title);
        Assert.StartsWith("dans 3 min", soon.Subtitle);
        Assert.Equal(0.6, soon.Progress!.Value, 3);
        Assert.Contains(soon.Actions, a => a.Id == MeetingFeature.JoinAction);

        now = start.AddMinutes(1);
        feature.Show(meeting);
        IslandActivity live = Assert.Single(activities.GetActiveActivities());
        Assert.StartsWith("Maintenant", live.Subtitle);
        Assert.Null(live.Progress);

        now = start.AddMinutes(20);
        feature.Show(meeting);
        Assert.Empty(activities.GetActiveActivities());
    }

    [Fact]
    public void TheQrCode_IsASquareWithFinderPatterns()
    {
        (bool[] modules, int size) = ShareFeature.Encode("http://192.168.1.20:50123/AAECAwQFBgcICQoLDA0ODw/photo.jpg");

        Assert.Equal(size * size, modules.Length);
        Assert.Equal(1, (size - 17) % 4 == 0 ? 1 : 0);
        Assert.True(size >= 25);

        // Motif de repérage en haut à gauche : un carré 7 × 7 plein sur son bord.
        Assert.All(Enumerable.Range(0, 7), i => Assert.True(modules[i] && modules[i * size]));
        Assert.False(modules[(1 * size) + 1]);
        Assert.True(modules[(3 * size) + 3]);
    }
}
