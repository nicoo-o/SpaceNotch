using System;
using SpaceNotch.Core.Productivity;
using Xunit;

namespace SpaceNotch.Core.Tests;

public sealed class MeasureClockTests
{
    private DateTimeOffset _now = new(2026, 9, 26, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_countdown_follows_the_wall_clock_even_across_sleep()
    {
        var clock = new MeasureClock(() => _now);
        clock.Set(TimeSpan.FromMinutes(25), countsDown: true);
        clock.Start();

        // Le capot est fermé 20 minutes : aucun battement, la valeur suit quand même.
        _now += TimeSpan.FromMinutes(20);
        Assert.Equal(TimeSpan.FromMinutes(5), clock.Value);

        _now += TimeSpan.FromMinutes(6);
        Assert.Equal(TimeSpan.Zero, clock.Value);
        Assert.True(clock.IsFinished);
    }

    [Fact]
    public void A_countdown_shows_the_last_second_until_it_is_over()
    {
        var clock = new MeasureClock(() => _now);
        clock.Set(TimeSpan.FromSeconds(10), countsDown: true);
        clock.Start();

        _now += TimeSpan.FromSeconds(9.2);
        Assert.Equal(TimeSpan.FromSeconds(1), clock.Value);
        Assert.False(clock.IsFinished);
    }

    [Fact]
    public void Pause_freezes_and_resume_continues()
    {
        var clock = new MeasureClock(() => _now);
        clock.Set(TimeSpan.Zero, countsDown: false);
        clock.Start();

        _now += TimeSpan.FromSeconds(30);
        clock.Pause();
        _now += TimeSpan.FromMinutes(10);
        Assert.Equal(TimeSpan.FromSeconds(30), clock.Value);

        clock.Start();
        _now += TimeSpan.FromSeconds(5);
        Assert.Equal(TimeSpan.FromSeconds(35), clock.Value);
    }
}

public sealed class BluetoothPayloadTests
{
    [Theory]
    [InlineData(14, true)]
    [InlineData(15, false)]
    [InlineData(null, false)]
    public void Battery_is_low_under_fifteen_percent(int? level, bool low)
        => Assert.Equal(low, new SpaceNotch.Core.Activities.BluetoothPayload("Casque", true, level, "audio").IsBatteryLow);
}
