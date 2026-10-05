using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Features.Menu;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Tableau de bord du clic au repos (ADR-028) : la recherche, puis quatre
/// tuiles qui mènent aux mêmes commandes que le menu rapide.
/// </summary>
public sealed class DashboardTilesTests
{
    [Fact]
    public void Quatre_tuiles_dans_l_ordre_choisi()
    {
        IReadOnlyList<QuickMenuTile> tiles = QuickMenuTiles.All();

        Assert.Equal(QuickMenuTiles.Max, tiles.Count);
        Assert.Equal(
            [QuickMenuFeature.TimerAction, QuickMenuFeature.ClipboardAction, QuickMenuFeature.NoteAction, QuickMenuFeature.MoreAction],
            tiles.Select(t => t.ActionId).ToArray());
    }

    [Fact]
    public void Le_minuteur_part_comme_la_ligne_du_menu_pour_15_minutes()
        => Assert.Equal("15", QuickMenuTiles.All()[0].Value);

    [Fact]
    public void Chaque_tuile_est_nommee_et_a_une_icone()
    {
        foreach (QuickMenuTile tile in QuickMenuTiles.All())
        {
            Assert.False(string.IsNullOrWhiteSpace(tile.Label), tile.ActionId);
            Assert.False(string.IsNullOrWhiteSpace(tile.IconKey), tile.ActionId);
        }
    }

    [Fact]
    public void Une_tuile_se_retrouve_par_sa_position_et_rien_au_dela()
    {
        Assert.Equal(QuickMenuFeature.NoteAction, QuickMenuTiles.At("2")?.ActionId);
        Assert.Null(QuickMenuTiles.At("4"));
        Assert.Null(QuickMenuTiles.At("x"));
        Assert.Null(QuickMenuTiles.At(null));
    }

    [Fact]
    public void Les_tuiles_ajoutent_leur_rangee_a_la_recherche_vide_sans_depasser_le_plafond()
    {
        double without = LauncherLayout.FootprintFor([]).Height;
        double with = LauncherLayout.FootprintFor([], tiles: true).Height;

        Assert.Equal(without + LauncherLayout.Tiles, with);

        var many = Enumerable.Range(0, 20)
            .Select(i => new LauncherResult($"r{i}", LauncherResultKind.Application, $"App {i}", string.Empty, $"r{i}", []))
            .ToList();
        IReadOnlyList<LauncherSection> sections = [new LauncherSection("Apps", many)];

        Assert.Equal(LauncherLayout.MaxHeight, LauncherLayout.FootprintFor(sections, tiles: true).Height);
    }
}
