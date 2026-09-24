using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Balayer une entrée du presse-papier pour la supprimer.</summary>
public class SwipeToDeleteTests
{
    private const double Width = 260;

    [Fact]
    public void AVerticalMoveScrollsTheListInsteadOfSwiping()
    {
        Assert.False(SwipeToDelete.Starts(6, 0));
        Assert.False(SwipeToDelete.Starts(10, 12));
        Assert.True(SwipeToDelete.Starts(-12, 3));
    }

    [Fact]
    public void TheRowFollowsTheHandLeftwardsAndResistsRightwards()
    {
        Assert.Equal(-80, SwipeToDelete.Offset(-80, Width));
        Assert.True(SwipeToDelete.Offset(60, Width) < 36);
        Assert.True(SwipeToDelete.Offset(60, Width) > 0);
        Assert.True(SwipeToDelete.Offset(-400, Width) > -Width - 36);
    }

    [Fact]
    public void FarEnoughDeletesWithoutMomentum()
    {
        Assert.Equal(SwipeOutcome.Delete, SwipeToDelete.Decide(-Width * 0.5, Width, 0));
        Assert.Equal(SwipeOutcome.Cancel, SwipeToDelete.Decide(-Width * 0.3, Width, 0));
    }

    [Fact]
    public void AQuickFlickDeletesEvenWhenShort()
    {
        Assert.Equal(SwipeOutcome.Delete, SwipeToDelete.Decide(-40, Width, -900));
        Assert.Equal(SwipeOutcome.Cancel, SwipeToDelete.Decide(-10, Width, -2000));
    }

    [Fact]
    public void ThrowingBackRightCancelsEvenPastTheThreshold()
        => Assert.Equal(SwipeOutcome.Cancel, SwipeToDelete.Decide(-Width * 0.6, Width, 900));

    [Fact]
    public void TheDeleteBackdropRevealsProgressively()
    {
        Assert.Equal(0, SwipeToDelete.Reveal(20, Width));
        Assert.Equal(0.5, SwipeToDelete.Reveal(-Width * SwipeToDelete.CommitFraction / 2, Width), 6);
        Assert.Equal(1, SwipeToDelete.Reveal(-Width, Width));
    }
}
