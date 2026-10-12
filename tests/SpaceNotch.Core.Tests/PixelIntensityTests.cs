using System;
using SpaceNotch.Core.Motion;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Intensité de Pixel (direction §7, point 9) : calme, normal, expressif.</summary>
public sealed class PixelIntensityTests
{
    [Fact]
    public void Normal_is_the_default_and_an_unknown_value_falls_back_to_it()
    {
        Assert.Equal(PixelIntensity.Normal, new AppSettings().PixelIntensity);

        var settings = new AppSettings { PixelIntensity = (PixelIntensity)42 };
        settings.Sanitize();

        Assert.Equal(PixelIntensity.Normal, settings.PixelIntensity);
    }

    [Fact]
    public void A_moving_pointer_keeps_the_gaze_and_a_still_one_lets_pixel_look_around()
    {
        Assert.Null(PixelGaze.Wander(TimeSpan.FromSeconds(3.9)));

        // Le tour du regard : à gauche, au centre, en haut à droite, au centre, puis encore.
        (double X, double Y) left = PixelGaze.Wander(PixelGaze.WanderAfter)!.Value;
        (double X, double Y) right = PixelGaze.Wander(PixelGaze.WanderAfter + (2 * PixelGaze.WanderStep))!.Value;
        (double X, double Y) again = PixelGaze.Wander(PixelGaze.WanderAfter + (4 * PixelGaze.WanderStep))!.Value;

        Assert.True(left.X < 0);
        Assert.True(right.X > 0 && right.Y < 0);
        Assert.Equal(left, again);
        Assert.Equal((0d, 0d), PixelGaze.Wander(PixelGaze.WanderAfter + PixelGaze.WanderStep)!.Value);

        // Jamais hors de la portée d'un regard ordinaire.
        Assert.InRange(Math.Abs(right.X), 0, PixelGaze.MaxLookX);
        Assert.InRange(Math.Abs(right.Y), 0, PixelGaze.MaxLookY);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(123)]
    public void An_expressive_pixel_yawns_twice_as_often(int seed)
    {
        Assert.Equal(PixelVitals.NextYawn(seed) / 2, PixelVitals.NextYawn(seed, PixelIntensity.Expressive));
        Assert.Equal(PixelVitals.NextYawn(seed), PixelVitals.NextYawn(seed, PixelIntensity.Normal));
    }
}
