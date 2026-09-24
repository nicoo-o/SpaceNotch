using SpaceNotch.Core.Presentation;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Le morphing : l'élément d'arrivée doit d'abord recouvrir exactement celui de
/// départ, puis revenir à sa place.
/// </summary>
public class MorphAnchorTests
{
    [Fact]
    public void TheArtwork_StartsExactlyWhereTheCompactOneWas()
    {
        var compact = new MorphRect(20, 10, 16, 16);
        var expanded = new MorphRect(12, 12, 76, 76);

        MorphTransform t = MorphTransform.Between(compact, expanded, uniform: false);

        // Coin supérieur gauche et taille, après transformation autour du coin.
        Assert.Equal(compact.X, expanded.X + t.TranslateX, 6);
        Assert.Equal(compact.Y, expanded.Y + t.TranslateY, 6);
        Assert.Equal(compact.Width, expanded.Width * t.ScaleX, 6);
        Assert.Equal(compact.Height, expanded.Height * t.ScaleY, 6);
    }

    [Fact]
    public void AText_ScalesUniformly_AndKeepsItsCentreLine()
    {
        var compact = new MorphRect(40, 12, 90, 16);
        var expanded = new MorphRect(100, 20, 120, 20);

        MorphTransform t = MorphTransform.Between(compact, expanded, uniform: true);

        Assert.Equal(t.ScaleX, t.ScaleY, 9);
        Assert.Equal(0.8, t.ScaleY, 6);
        Assert.Equal(compact.CenterY, expanded.Y + t.TranslateY + (expanded.Height * t.ScaleY / 2), 6);
        Assert.Equal(compact.X, expanded.X + t.TranslateX, 6);
    }

    [Fact]
    public void AnEmptyRectangle_DoesNotMorph()
    {
        Assert.True(MorphTransform.Between(new MorphRect(0, 0, 0, 10), new MorphRect(5, 5, 10, 10), false).IsIdentity);
        Assert.True(MorphTransform.Between(new MorphRect(0, 0, 10, 10), new MorphRect(5, 5, 10, 0), true).IsIdentity);
    }
}
