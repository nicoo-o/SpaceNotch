using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Repos étroit (session du 2026-10-04) : au repos, la notch reste la lèvre de
/// 80 × 18 DIP ; elle ne s'élargit que pour l'heure et la météo, et aucune
/// forme du repos n'est plus étroite que le repos lui-même.
/// </summary>
public sealed class RestFootprintTests
{
    [Fact]
    public void Une_forme_au_moins_aussi_grande_qu_une_autre_prend_le_maximum_de_chaque_dimension()
    {
        var clock = new IslandFootprint(150, 30);
        IslandFootprint coveredRest = CameraCutout.Cover(IslandFootprint.Idle, CameraCutout.DefaultWidth);

        // Avec une encoche réelle, l'heure ne doit pas rétrécir sous le repos.
        Assert.Equal(new IslandFootprint(200, 30), clock.AtLeast(coveredRest));
        Assert.Equal(clock, clock.AtLeast(IslandFootprint.Idle));
    }

    [Fact]
    public void Sans_encoche_le_repos_reste_la_levre_de_80_sur_18()
    {
        Assert.Equal(new IslandFootprint(80, 18), IslandFootprint.Idle);
    }
}
