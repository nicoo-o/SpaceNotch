using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Platform.Windows.Notifications;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Ne pas déranger (F9) : au calme, les notifications sont comptées sans rien
/// montrer ; à la sortie, la notch les résume.
/// </summary>
public class QuietModeTests
{
    [Fact]
    public void TheMoon_HasItsPixels()
    {
        Assert.NotNull(PixelGlyphs.Resolve("Moon"));
    }

    [Fact]
    public void Quiet_HoldsNotifications_ThenSummarisesThem()
    {
        bool quiet = false;
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => quiet);

        quiet = true;
        feature.RefreshQuiet();

        IslandActivity moon = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(NotificationFeature.QuietActivityId, moon.Id);
        Assert.Equal("Moon", moon.IconKey);
        Assert.Null(moon.Metric);

        feature.Receive("Slack", "Alice", "Réunion ?");
        feature.Receive("Slack", "Bob", "Déploiement fait");
        feature.Receive("Mail", "Facture", "…");

        // Rien ne s'affiche : seule la lune, qui porte le compte.
        moon = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(NotificationFeature.QuietActivityId, moon.Id);
        Assert.Equal("3", moon.Metric);

        quiet = false;
        feature.RefreshQuiet();

        IslandActivity summary = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(NotificationFeature.QuietSummaryActivityId, summary.Id);
        Assert.Equal(IslandSceneCatalog.Quiet, summary.SceneKey);
        QuietPayload payload = Assert.IsType<QuietPayload>(summary.Payload);
        Assert.Equal(3, payload.Total);
        Assert.Equal("Slack", payload.Groups[0].App);
        Assert.Equal(2, payload.Groups[0].Count);
        Assert.Equal("Bob", payload.Groups[0].Latest);
    }

    [Fact]
    public void AQuietStretch_WithNothingHeld_LeavesNothingBehind()
    {
        bool quiet = true;
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => quiet);

        feature.RefreshQuiet();
        quiet = false;
        feature.RefreshQuiet();

        Assert.Empty(activities.GetActiveActivities());
    }

    [Fact]
    public void OutsideQuiet_NotificationsShowAsUsual()
    {
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => false);

        feature.RefreshQuiet();
        feature.Receive("Slack", "Alice", "Réunion ?");

        IslandActivity shown = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(IslandSceneCatalog.Notification, shown.SceneKey);
    }
}
