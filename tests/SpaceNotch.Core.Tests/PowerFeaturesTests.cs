using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Power;
using SpaceNotch.Platform.Windows.Power;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Charge (F1) et moniteur système (F6), sans Windows.</summary>
public class PowerFeaturesTests
{
    [Fact]
    public void PluggingIn_ShowsTheBatteryOnce()
    {
        var activities = new ActivityManager();
        var feature = new ChargeFeature(activities, new EventBus(), watcher: null);

        feature.Report(hasBattery: true, plugged: false, percent: 41);
        Assert.Empty(activities.GetActiveActivities());

        feature.Report(hasBattery: true, plugged: true, percent: 42);
        IslandActivity charge = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Bolt", charge.IconKey);
        Assert.Equal(42, Assert.IsType<ChargePayload>(charge.Payload).Percent);
        Assert.Equal(0.42, charge.Progress!.Value, 3);
        Assert.NotNull(charge.Duration);
    }

    [Fact]
    public void ASustainedLoad_RaisesThenClearsTheAlert()
    {
        var activities = new ActivityManager();
        var feature = new SystemMonitorFeature(activities, new EventBus());
        var t = DateTimeOffset.UnixEpoch;
        var chrome = new HeavyProcess("chrome", 4242, 71.5);

        for (int s = 0; s < 20; s += 2)
        {
            feature.Add(95, t.AddSeconds(s), chrome);
            Assert.Empty(activities.GetActiveActivities());
        }

        feature.Add(95, t.AddSeconds(20), chrome);
        IslandActivity alert = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(IslandSceneCatalog.Monitor, alert.SceneKey);
        Assert.Equal("chrome", alert.Title);
        Assert.Contains(alert.Actions, a => a.Id == SystemMonitorFeature.CloseAction);
        Assert.Equal(4242, Assert.IsType<MonitorPayload>(alert.Payload).ProcessId);

        feature.Add(30, t.AddSeconds(22));
        feature.Add(30, t.AddSeconds(31));
        Assert.Empty(activities.GetActiveActivities());
    }
}
