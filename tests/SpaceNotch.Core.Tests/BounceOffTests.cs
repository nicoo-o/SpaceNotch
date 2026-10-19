using SpaceNotch.Core.Motion;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// n° 91 : « Rebond » éteint garde le ressort de la forme, sans dépassement,
/// au lieu d'un saut suivi d'un fondu.
/// </summary>
public sealed class BounceOffTests
{
    [Fact]
    public void Bounce_on_keeps_the_chosen_damping()
    {
        var settings = new AppSettings { AllowBouncyAnimations = true };

        Assert.True(settings.Motion.DampingRatio < 1.0);
        Assert.True(settings.HoverMotion.DampingRatio < 1.0);
    }

    [Fact]
    public void Bounce_off_is_a_critically_damped_spring_with_the_same_response()
    {
        var on = new AppSettings { AllowBouncyAnimations = true };
        var off = new AppSettings { AllowBouncyAnimations = false };

        Assert.Equal(1.0, off.Motion.DampingRatio, 3);
        Assert.Equal(1.0, off.HoverMotion.DampingRatio, 3);
        Assert.Equal(on.Motion.ResponseSeconds, off.Motion.ResponseSeconds, 3);
    }

    [Fact]
    public void Bounce_off_also_closes_and_announces_priorities_without_overshoot()
    {
        var off = new AppSettings { AllowBouncyAnimations = false };

        Assert.True(MotionPresets.CloseOf(off.Motion).DampingRatio >= 1.0 - 1e-6);
        Assert.True(MotionPresets.ForPriority(off.Motion, SpaceNotch.Core.Activities.ActivityPriority.Critical).DampingRatio >= 1.0 - 1e-6);
    }
}
