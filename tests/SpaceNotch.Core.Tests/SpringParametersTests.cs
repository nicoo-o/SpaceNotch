using SpaceNotch.Core.Animation;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class SpringParametersTests
{
    [Fact]
    public void FromSettings_KeepsReasonableValues()
    {
        SpringParameters parameters = SpringParameters.FromSettings(260, 24, 1.2);

        Assert.Equal(260, parameters.Stiffness);
        Assert.Equal(24, parameters.Damping);
        Assert.Equal(1.2, parameters.Mass);
    }

    [Fact]
    public void FromSettings_ClampsAbsurdValues()
    {
        // Une configuration éditée à la main ne doit pas produire un mouvement
        // absurde ni une division par zéro dans le solveur.
        SpringParameters tooSoft = SpringParameters.FromSettings(0, 0, 0);
        SpringParameters tooHard = SpringParameters.FromSettings(100000, 100000, 100000);

        Assert.True(tooSoft.Stiffness >= 40);
        Assert.True(tooSoft.Damping >= 4);
        Assert.True(tooSoft.Mass > 0);

        Assert.True(tooHard.Stiffness <= 600);
        Assert.True(tooHard.Mass <= 4);
    }

    [Fact]
    public void FromSettings_FallsBackOnNaN()
    {
        SpringParameters parameters = SpringParameters.FromSettings(double.NaN, double.NaN, double.NaN);

        Assert.Equal(SpringParameters.Default.Stiffness, parameters.Stiffness);
        Assert.Equal(SpringParameters.Default.Damping, parameters.Damping);
        Assert.Equal(SpringParameters.Default.Mass, parameters.Mass);
    }

    [Fact]
    public void Bouncy_IsLessDampedThanDefault()
    {
        Assert.True(SpringParameters.Bouncy.Damping < SpringParameters.Default.Damping);
    }

    [Fact]
    public void Calm_IsMoreDampedThanDefault()
    {
        // Le réglage sans mouvement doit converger sans osciller.
        Assert.True(SpringParameters.Calm.Damping > SpringParameters.Default.Damping);
    }
}
