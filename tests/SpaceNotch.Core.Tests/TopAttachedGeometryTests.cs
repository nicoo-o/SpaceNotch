using System;
using System.Linq;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Règle n°1 de SpaceNotch : une notch attachée au bord supérieur de l'écran,
/// jamais une capsule flottante. Ces tests tiennent cette règle là où elle se
/// calcule, pour qu'aucune évolution ne puisse la casser sans le dire.
/// </summary>
public class TopAttachedGeometryTests
{
    private const double Tolerance = 0.001;

    [Fact]
    public void ThereIsOnlyOneGeometryMode_TopAttached()
    {
        // Ajouter un mode — une capsule flottante, par exemple — doit casser ce
        // test, et obliger à relire ADR-017 avant d'aller plus loin.
        Assert.Equal([IslandGeometryMode.TopAttached], Enum.GetValues<IslandGeometryMode>());
        Assert.Equal(IslandGeometryMode.TopAttached, NotchGeometry.Mode);
        Assert.Equal(IslandGeometryMode.TopAttached, AppSettings.GeometryMode);
    }

    [Theory]
    [InlineData(80, 18)]
    [InlineData(148, 34)]
    [InlineData(256, 52)]
    [InlineData(396, 140)]
    public void TheTopEdgeSpansTheWholeWidthOnTheScreenEdge(double width, double height)
    {
        ShapePoint[] points = NotchGeometry.Default.Silhouette(new IslandFootprint(width, height));

        // Aucun point au-dessus du bord : la forme ne sort jamais de l'écran.
        Assert.All(points, point => Assert.True(point.Y >= -Tolerance, $"Point au-dessus du bord : {point}"));

        // Les deux extrémités du bord supérieur sont sur l'écran, à zéro, aux
        // deux bouts de la largeur : aucune marge entre l'écran et la notch.
        Assert.Contains(points, point => Math.Abs(point.X) < Tolerance && Math.Abs(point.Y) < Tolerance);
        Assert.Contains(points, point => Math.Abs(point.X - width) < Tolerance && Math.Abs(point.Y) < Tolerance);

        // Et elle descend jusqu'à sa hauteur : c'est bien une forme accrochée.
        Assert.Contains(points, point => Math.Abs(point.Y - height) < Tolerance);
    }

    [Fact]
    public void TheLegacyTopOffsetIsAlwaysResetToZero()
    {
        // Une configuration ancienne pouvait décaler la forme vers le bas, ce qui
        // la transformait en capsule flottante. Elle est ramenée au bord.
        var settings = new AppSettings { TopOffset = 42 };

        settings.Sanitize();

        Assert.Equal(0, settings.TopOffset);

        settings.TopOffset = -12;
        settings.Sanitize();

        Assert.Equal(0, settings.TopOffset);
    }

    [Fact]
    public void Shoulders_AreConcaveAndTangentToTheScreenEdge()
    {
        // Épaule gauche de 8 sur une forme de 148 × 34 : elle part du bord de
        // l'écran en (0, 0) et rejoint le flanc en (8, 8). Au milieu de sa
        // course, un congé concave passe *au-dessus* de la diagonale qui relie
        // ses extrémités — c'est la matière qui « coule » du bord dans la notch.
        ShapePoint[] points = IslandShape.Silhouette(148, 34, 26, IslandShape.Circular, shoulder: 8);

        Assert.Contains(points, point => Math.Abs(point.X - 8) < Tolerance && Math.Abs(point.Y - 8) < Tolerance);
        Assert.Contains(points, point => Math.Abs(point.X - 140) < Tolerance && Math.Abs(point.Y - 8) < Tolerance);

        ShapePoint[] leftShoulder = points.Where(point => point.X < 8 - Tolerance && point.Y > Tolerance && point.Y < 8 - Tolerance).ToArray();

        Assert.NotEmpty(leftShoulder);
        Assert.All(leftShoulder, point => Assert.True(point.X > point.Y, $"Épaule convexe au lieu de concave : {point}"));
    }

    [Fact]
    public void Shoulders_AddExactlyTheExpectedPoints()
    {
        Assert.Equal(IslandShape.PointCount, IslandShape.Silhouette(240, 52, 12).Length);
        Assert.Equal(IslandShape.PointCountWithShoulders, IslandShape.Silhouette(240, 52, 26, shoulder: 8).Length);
    }

    [Fact]
    public void TheBodyStaysInsideItsFootprint()
    {
        ShapePoint[] points = IslandShape.Silhouette(396, 140, 34, IslandShape.Squircle, shoulder: 8);

        Assert.All(points, point =>
        {
            Assert.InRange(point.X, -Tolerance, 396 + Tolerance);
            Assert.InRange(point.Y, -Tolerance, 140 + Tolerance);
        });
    }

    [Fact]
    public void ACompactNotch_CarriesAGenerousCorner()
    {
        // Un rectangle arrondi plafonnerait le congé à la moitié de la hauteur,
        // soit 17 sur une forme de 34. La notch n'a qu'un bord libre : son congé
        // peut occuper toute la hauteur sous les épaules. C'est ce qui donne les
        // grands arrondis organiques demandés, même en compact.
        double radius = NotchGeometry.Default.RadiusFor(IslandFootprint.Signal);

        Assert.True(radius >= 24, $"Congé compact trop sage : {radius}");
        Assert.True(radius <= IslandFootprint.Signal.Height - NotchGeometry.DefaultShoulder + Tolerance);
    }

    [Fact]
    public void TheRadiusGrowsWithTheShape_WithoutJumping()
    {
        NotchGeometry geometry = NotchGeometry.Default;
        double previous = geometry.RadiusFor(new IslandFootprint(400, 34));

        // Le rayon suit la hauteur continûment : aucun saut d'une image à la
        // suivante pendant un morphing, et il atteint la valeur ouverte.
        for (double height = 35; height <= 200; height += 1)
        {
            double radius = geometry.RadiusFor(new IslandFootprint(400, height));

            Assert.True(radius >= previous - Tolerance, $"Le rayon recule à {height} : {radius} < {previous}");
            Assert.True(radius - previous <= 1.0 + Tolerance, $"Saut de rayon à {height} : {previous} → {radius}");

            previous = radius;
        }

        Assert.Equal(NotchGeometry.DefaultExpandedRadius, previous, 3);
    }

    [Fact]
    public void AnExtremeRadius_IsClampedToWhatTheShapeCanCarry()
    {
        ShapePoint[] points = IslandShape.Silhouette(100, 20, 500, IslandShape.Squircle, shoulder: 8);

        Assert.NotEmpty(points);
        Assert.All(points, point => Assert.InRange(point.Y, -Tolerance, 20 + Tolerance));
        Assert.True(IslandShape.EffectiveShoulder(100, 20, 8) <= 20 / 3.0 + Tolerance);
    }

    [Fact]
    public void TheSpecularBand_CutsTheShapeWithShoulders()
    {
        ShapePoint[] points = IslandShape.Silhouette(256, 52, 28, IslandShape.Squircle, band: 20, shoulder: 8);

        Assert.All(points, point => Assert.True(point.Y <= 20 + Tolerance));
        Assert.Contains(points, point => Math.Abs(point.Y - 20) < Tolerance);
    }

    [Fact]
    public void ThePreview_IsASlightGrowth_NeverAJump()
    {
        // Le survol fait descendre et élargir légèrement la notch : jamais plus
        // petit que la forme de départ, jamais plus de 25 % plus large.
        foreach (IslandPresentationTier tier in Enum.GetValues<IslandPresentationTier>())
        {
            IslandFootprint rest = IslandFootprint.For(tier);
            IslandFootprint preview = IslandFootprint.PreviewOf(tier);

            Assert.True(preview.Width >= rest.Width);
            Assert.True(preview.Height >= rest.Height);
            Assert.True(preview.Width <= rest.Width * 1.25, $"Aperçu trop large pour {tier} : {preview.Width} contre {rest.Width}");
        }
    }

    [Fact]
    public void TheCompactHeight_StaysInTheIntendedRange()
    {
        Assert.InRange(IslandFootprint.Signal.Height, 32, 40);
        Assert.InRange(IslandFootprint.PreviewOf(IslandPresentationTier.Signal).Height, 40, 60);
    }

    [Fact]
    public void TheWidth_FollowsTheContent_WithinTheNotchIdentity()
    {
        // La largeur suit le texte, comme dans la référence…
        IslandFootprint shortText = IslandFootprint.Fit(IslandPresentationTier.Card, 120, 12);
        IslandFootprint longText = IslandFootprint.Fit(IslandPresentationTier.Card, 200, 12);

        Assert.True(longText.Width > shortText.Width);
        Assert.Equal(IslandFootprint.Card.Height, longText.Height);
        Assert.Equal(200 + 28 + 24, longText.Width);

        // … sans jamais devenir une barre ni une grosse fenêtre.
        Assert.Equal(IslandFootprint.MinimumWidth(IslandPresentationTier.Signal), IslandFootprint.Fit(IslandPresentationTier.Signal, 10, 12).Width);
        Assert.Equal(IslandFootprint.MaximumWidth(IslandPresentationTier.Card), IslandFootprint.Fit(IslandPresentationTier.Card, 5000, 12).Width);

        // La veille ne s'ajuste pas : elle n'a rien à porter.
        Assert.Equal(IslandFootprint.Idle, IslandFootprint.Fit(IslandPresentationTier.Idle, 300, 12));
    }

    [Fact]
    public void ThePreviewOfAFittedShape_GrowsFromItsRealWidth()
    {
        IslandFootprint rest = IslandFootprint.Fit(IslandPresentationTier.Card, 240, 12);
        IslandFootprint preview = IslandFootprint.PreviewOf(IslandPresentationTier.Card, rest);

        Assert.True(preview.Width > rest.Width);
        Assert.True(preview.Width <= rest.Width * 1.25);
    }
}
