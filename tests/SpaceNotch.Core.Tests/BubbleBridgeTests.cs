using System.Linq;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>La bulle qui naît de la notch comme une goutte, et y rentre.</summary>
public class BubbleBridgeTests
{
    private static readonly ScreenRect Notch = new(800, 0, 280, 52);

    [Fact]
    public void AtTheEndTheBubbleIsInPlaceAndFree()
    {
        var final = new ScreenRect(1088, 0, 54, 36);
        BubbleBridgeFrame frame = BubbleBridge.Frame(Notch, final, 1);

        Assert.Equal(final, frame.Bubble);
        Assert.Equal(1, frame.Scale, 6);
        Assert.Empty(frame.Bridge);
    }

    [Fact]
    public void AtTheStartTheBubbleIsInsideTheNotchFlank()
    {
        var final = new ScreenRect(1088, 0, 54, 36);
        BubbleBridgeFrame frame = BubbleBridge.Frame(Notch, final, 0);

        Assert.True(frame.Bubble.X < Notch.Right, "Elle sort du flanc de la notch.");
        Assert.Equal(BubbleBridge.BirthScale, frame.Scale, 6);
    }

    [Fact]
    public void MidwayAThreadStillHoldsItToTheNotch()
    {
        var final = new ScreenRect(1088, 0, 54, 36);
        BubbleBridgeFrame frame = BubbleBridge.Frame(Notch, final, 0.3);

        ShapePoint[] thread = Assert.Single(frame.Bridge);
        Assert.True(thread.Min(p => p.X) < Notch.Right, "Le fil part de l'intérieur de la notch.");
        Assert.True(thread.Max(p => p.X) > frame.Bubble.X, "Et entre dans la bulle.");
        Assert.True(GooBridge.SignedArea(thread) > 0);
    }

    [Theory]
    [InlineData(700, 0)]     // à gauche
    [InlineData(1100, 0)]    // à droite
    [InlineData(820, 70)]    // en dessous (languette)
    public void EveryDirectionEndsExactlyInPlace(double x, double y)
    {
        var final = new ScreenRect(x, y, 40, 40);
        Assert.Equal(final, BubbleBridge.Frame(Notch, final, 1).Bubble);

        foreach (double p in new[] { 0.1, 0.3, 0.5 })
        {
            foreach (ShapePoint[] piece in BubbleBridge.Frame(Notch, final, p).Bridge)
            {
                Assert.True(GooBridge.SignedArea(piece) > 0);
            }
        }
    }

    [Fact]
    public void TheBubbleNeverShrinksWhileBeingBorn()
    {
        var final = new ScreenRect(1088, 0, 54, 36);
        double previous = 0;

        for (double p = 0; p <= 1.0001; p += 0.05)
        {
            double scale = BubbleBridge.Frame(Notch, final, p).Scale;
            Assert.True(scale >= previous - 1e-9);
            previous = scale;
        }
    }
}
