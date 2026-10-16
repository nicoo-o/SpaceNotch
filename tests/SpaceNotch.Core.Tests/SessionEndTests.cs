using System;
using System.Linq;
using System.Threading;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Features.Productivity;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// La fin d'une mesure (n° 86) : jouée avec une horloge injectée et un battement
/// appelé à la main, au lieu d'attendre le vrai.
/// </summary>
public sealed class SessionEndTests
{
    private DateTimeOffset _now = new(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async System.Threading.Tasks.Task A_finished_countdown_says_so_then_offers_a_fresh_start()
    {
        var activities = new ActivityManager();
        await using var timer = new TimerFeature(activities, new EventBus(), now: () => _now) { TickPeriod = Timeout.InfiniteTimeSpan };

        timer.StartCountdown(TimeSpan.FromSeconds(10));
        _now += TimeSpan.FromSeconds(11);
        timer.OnTick(null);

        IslandActivity done = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Temps écoulé", done.Title);
        var payload = Assert.IsType<TimerPayload>(done.Payload);
        Assert.Equal(TimeSpan.Zero, payload.Remaining);
        Assert.Equal(TimerToggle.Start, payload.Toggle);
        Assert.False(timer.IsMeasuring);

        // Relance : un compte neuf de 5 min, pas un rebours de 0 s fini aussitôt (relecture n° 78).
        timer.Toggle();
        _now += TimeSpan.FromSeconds(1);
        timer.OnTick(null);

        Assert.True(timer.IsMeasuring);
        var again = Assert.IsType<TimerPayload>(Assert.Single(activities.GetActiveActivities()).Payload);
        Assert.Equal(TimeSpan.FromMinutes(4) + TimeSpan.FromSeconds(59), again.Remaining);
        Assert.Equal(TimerToggle.Pause, again.Toggle);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_running_countdown_only_redraws_before_its_end()
    {
        var activities = new ActivityManager();
        await using var timer = new TimerFeature(activities, new EventBus(), now: () => _now) { TickPeriod = Timeout.InfiniteTimeSpan };

        timer.StartCountdown(TimeSpan.FromSeconds(10));
        _now += TimeSpan.FromSeconds(3);
        timer.OnTick(null);

        var payload = Assert.IsType<TimerPayload>(Assert.Single(activities.GetActiveActivities()).Payload);
        Assert.Equal(TimeSpan.FromSeconds(7), payload.Remaining);
        Assert.True(timer.IsMeasuring);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_finished_focus_session_ends_stopped_and_ready_to_start()
    {
        var activities = new ActivityManager();
        await using var pomodoro = new PomodoroFeature(activities, new EventBus(), now: () => _now) { TickPeriod = Timeout.InfiniteTimeSpan };

        pomodoro.Start(TimeSpan.FromMinutes(10));
        Assert.Equal(TimeSpan.FromMinutes(10), pomodoro.SessionLength);
        _now += TimeSpan.FromMinutes(11);
        pomodoro.OnTick(null);

        IslandActivity done = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Session terminée", done.Title);

        // Avant n° 78, cette activité n'avait pas de contenu : la scène gardait « Suspendre ».
        var payload = Assert.IsType<TimerPayload>(done.Payload);
        Assert.Equal(TimerToggle.Start, payload.Toggle);
        Assert.False(pomodoro.IsSessionRunning);
        Assert.Equal(TimeSpan.FromMinutes(25), pomodoro.SessionLength);   // revenue au défaut
    }

    [Fact]
    public async System.Threading.Tasks.Task A_paused_focus_session_does_not_end_on_its_own()
    {
        var activities = new ActivityManager();
        await using var pomodoro = new PomodoroFeature(activities, new EventBus(), now: () => _now) { TickPeriod = Timeout.InfiniteTimeSpan };

        pomodoro.Start(TimeSpan.FromMinutes(25));
        _now += TimeSpan.FromMinutes(5);
        pomodoro.Pause();
        _now += TimeSpan.FromHours(1);
        pomodoro.OnTick(null);

        Assert.Equal(TimeSpan.FromMinutes(20), pomodoro.Remaining);
        Assert.NotEqual("Session terminée", Assert.Single(activities.GetActiveActivities()).Title);
    }

    [Fact]
    public async System.Threading.Tasks.Task A_paused_timer_is_still_a_measure_to_keep()
    {
        // n° 62 : la tuile « 15 min » écrasait un minuteur en pause.
        await using var timer = new TimerFeature(new ActivityManager(), new EventBus(), now: () => _now) { TickPeriod = Timeout.InfiniteTimeSpan };
        Assert.False(timer.HasMeasure);

        timer.StartCountdown(TimeSpan.FromMinutes(5));
        _now += TimeSpan.FromMinutes(1);
        timer.Toggle();

        Assert.False(timer.IsMeasuring);
        Assert.True(timer.HasMeasure);

        timer.Reset();
        Assert.False(timer.HasMeasure);
    }
}
