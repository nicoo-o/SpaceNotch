using NotchFlow.Core.Animation;
using Xunit;

namespace NotchFlow.Core.Tests;

public class SpringSolverTests
{
    [Fact]
    public void SpringSolver_InitialValue_MatchesStart()
    {
        var solver = new SpringSolver(SpringParameters.Default);
        var (value, _) = solver.Evaluate(0.0, 180, 380);

        Assert.Equal(180, value);
    }

    [Fact]
    public void SpringSolver_SettlesNearTarget_AfterTime()
    {
        var solver = new SpringSolver(SpringParameters.Default);

        // Après 1 seconde, le ressort standard doit avoir convergé vers 380
        var (value, _) = solver.Evaluate(1.0, 180, 380);

        Assert.InRange(value, 378, 382);
        Assert.True(solver.HasSettled(1.5, 180, 380, threshold: 0.5));
    }
}
