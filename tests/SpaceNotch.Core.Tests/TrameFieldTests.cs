using System;
using System.Linq;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Trame des scènes ouvertes (B3) : monte du bas, plus dense au centre, jamais
/// derrière le contenu, jamais hors de la silhouette.
/// </summary>
public class TrameFieldTests
{
    private static readonly TrameRect[] Nothing = [];

    [Fact]
    public void TheTopOfTheScene_StaysBlack()
    {
        var cells = TrameField.Cells(570, 225, 4.5, 48, 0, Nothing);

        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.True((c.Row + 0.5) * 4.5 > 225 * 0.4));
    }

    [Fact]
    public void TheBottomCentre_IsDenserThanTheSides()
    {
        double centre = TrameField.Density(285, 215, 570, 225);
        double side = TrameField.Density(40, 215, 570, 225);

        Assert.True(centre > side * 2);
    }

    [Fact]
    public void Content_KeepsItsBlackMargin()
    {
        var player = new TrameRect(100, 120, 470, 225);

        var cells = TrameField.Cells(570, 225, 4.5, 48, 0, [player]);

        Assert.DoesNotContain(cells, c => player.Contains((c.Column + 0.5) * 4.5, (c.Row + 0.5) * 4.5));
    }

    [Fact]
    public void TheTrame_StartsBelowTheLowestContent_NeverBesideIt()
    {
        // Un titre étroit à gauche : la trame ne remplit pas le vide à sa droite.
        var title = new TrameRect(20, 100, 200, 170);

        var cells = TrameField.Cells(570, 225, 4.5, 48, 0, [title]);

        Assert.NotEmpty(cells);
        Assert.All(cells, c => Assert.True((c.Row + 0.5) * 4.5 > 170));
    }

    [Fact]
    public void TheRoundedCorners_AreRespected()
    {
        var cells = TrameField.Cells(570, 225, 4.5, 60, 18, Nothing);

        // Rien dans les épaules, rien dans l'angle arrondi en bas à gauche.
        Assert.DoesNotContain(cells, c => (c.Column + 0.5) * 4.5 < 18);
        Assert.DoesNotContain(cells, c => (c.Column + 0.5) * 4.5 < 30 && (c.Row + 0.5) * 4.5 > 215);
    }

    [Fact]
    public void Music_PushesTheTrameUp()
    {
        int quiet = TrameField.Cells(570, 225, 4.5, 48, 0, Nothing, level: 0).Count;
        int loud = TrameField.Cells(570, 225, 4.5, 48, 0, Nothing, level: 1).Count;

        Assert.True(loud > quiet);
    }

    [Fact]
    public void TheLevel_RisesFast_AndFallsSlowly()
    {
        double up = TrameField.Smooth(0, 1);
        double down = TrameField.Smooth(1, 0);

        Assert.True(up > 0.5);
        Assert.True(down > 0.8);
    }

    [Fact]
    public void AnEmptyScene_HasNoTrame()
        => Assert.Empty(TrameField.Cells(0, 0, 4.5, 10, 0, Nothing));
}
