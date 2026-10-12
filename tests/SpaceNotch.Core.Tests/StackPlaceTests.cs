using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Features.Productivity;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 88 : un minuteur et un focus qui tournent ensemble. Chaque seconde, le
/// battement republie l'activité ; avec une date neuve, les deux passaient tour
/// à tour en tête de pile et la notch repliée alternait chaque seconde.
/// </summary>
public sealed class StackPlaceTests
{
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task A_tick_does_not_push_the_timer_in_front_of_the_focus()
    {
        var activities = new ActivityManager();
        await using var timer = new TimerFeature(activities, new EventBus(), now: () => _now);
        await using var focus = new PomodoroFeature(activities, new EventBus(), now: () => _now);

        timer.StartCountdown(TimeSpan.FromMinutes(10));
        _now += TimeSpan.FromSeconds(1);
        focus.Start();
        Assert.Equal(PomodoroFeature.FeatureKey, activities.CurrentActivity?.FeatureId);

        // Deux secondes depuis le départ : le vrai battement (250 ms) republie le minuteur à 09:58.
        _now += TimeSpan.FromSeconds(1);
        var watch = Stopwatch.StartNew();

        while (activities.GetActiveActivities() is var all
            && !all.Any(a => a.FeatureId == TimerFeature.FeatureKey && a.Title == "09:58")
            && watch.Elapsed < TimeSpan.FromSeconds(3))
        {
            await Task.Delay(50);
        }

        Assert.Contains(activities.GetActiveActivities(), a => a.FeatureId == TimerFeature.FeatureKey && a.Title == "09:58");
        Assert.Equal(PomodoroFeature.FeatureKey, activities.CurrentActivity?.FeatureId);
    }

    [Fact]
    public async Task A_gesture_brings_the_timer_back_in_front()
    {
        var activities = new ActivityManager();
        await using var timer = new TimerFeature(activities, new EventBus(), now: () => _now);
        await using var focus = new PomodoroFeature(activities, new EventBus(), now: () => _now);

        timer.StartCountdown(TimeSpan.FromMinutes(10));
        _now += TimeSpan.FromSeconds(1);
        focus.Start();

        _now += TimeSpan.FromSeconds(1);
        timer.Toggle();   // suspendre : un geste sur le minuteur

        Assert.Equal(TimerFeature.FeatureKey, activities.CurrentActivity?.FeatureId);
    }
}
