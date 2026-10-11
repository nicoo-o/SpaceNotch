using System;
using SpaceNotch.Core.SystemInfo;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>n° 74 : une seule mesure du processeur pour le moniteur et Pixel.</summary>
public sealed class SharedReadingTests
{
    private long _now = 1_000;
    private int _measures;

    private SharedReading Reading() => new(() => ++_measures * 10.0, () => _now);

    [Fact]
    public void A_recent_enough_measure_is_reused()
    {
        SharedReading reading = Reading();

        Assert.Equal(10, reading.Get(TimeSpan.Zero));     // le moniteur mesure
        _now += 2_000;
        Assert.Equal(10, reading.Get(TimeSpan.FromSeconds(5.5)));   // Pixel la reprend
        Assert.Equal(1, _measures);
    }

    [Fact]
    public void A_stale_measure_is_redone_for_everyone()
    {
        SharedReading reading = Reading();

        reading.Get(TimeSpan.Zero);
        _now += 6_000;
        Assert.Equal(20, reading.Get(TimeSpan.FromSeconds(5.5)));
        Assert.Equal(2, _measures);
    }

    [Fact]
    public void Zero_always_measures_and_the_first_read_always_measures()
    {
        SharedReading reading = Reading();

        Assert.Equal(10, reading.Get(TimeSpan.FromHours(1)));
        Assert.Equal(20, reading.Get(TimeSpan.Zero));
        Assert.Equal(20, reading.Get(TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Monitor_and_Pixel_together_make_a_single_sampler()
    {
        // Moniteur toutes les 5 s, Pixel toutes les 2 s, pendant une minute.
        SharedReading reading = Reading();

        for (long t = 0; t <= 60_000; t += 1_000)
        {
            _now = 1_000 + t;

            if (t % 5_000 == 0)
            {
                reading.Get(TimeSpan.Zero);
            }

            if (t % 2_000 == 0)
            {
                reading.Get(TimeSpan.FromSeconds(5.5));
            }
        }

        // Une mesure par passage du moniteur (13), aucune pour Pixel ; avant n° 74 : 13 + 31.
        Assert.Equal(13, _measures);
    }
}
