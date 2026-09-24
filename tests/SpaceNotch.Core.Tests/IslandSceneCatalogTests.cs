using System.Linq;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

public class IslandSceneCatalogTests
{
    [Fact]
    public void Catalog_UnknownSceneFallsBackToCollapsed()
    {
        // Une fonctionnalité mal configurée ne doit jamais pouvoir faire
        // disparaître l'Island : la clé inconnue retombe sur la forme au repos.
        IslandFootprint footprint = IslandSceneCatalog.FootprintFor("scene.qui.n.existe.pas");

        Assert.Equal(IslandFootprint.Collapsed, footprint);
    }

    [Fact]
    public void Catalog_NullSceneFallsBackToCollapsed()
    {
        Assert.Equal(IslandFootprint.Collapsed, IslandSceneCatalog.FootprintFor(null));
    }

    [Fact]
    public void Catalog_EveryDeclaredSceneHasValidFootprint()
    {
        IslandFootprint[] footprints = IslandSceneCatalog.AllKeys
            .Select(IslandSceneCatalog.FootprintFor)
            .ToArray();

        Assert.NotEmpty(footprints);
        Assert.All(footprints, footprint => Assert.True(footprint.IsValid));
    }

    [Fact]
    public void Catalog_DeclaredScenesAreResolvable()
    {
        Assert.True(IslandSceneCatalog.IsKnown(IslandSceneCatalog.Media));
        Assert.True(IslandSceneCatalog.IsKnown(IslandSceneCatalog.Pill));
        Assert.False(IslandSceneCatalog.IsKnown("inconnue"));
    }

    [Fact]
    public void Catalog_CardSceneIsDeclaredForThirdPartyContent()
    {
        // La carte générique est le contrat d'affichage des greffons tiers : sans
        // elle, un auteur n'aurait d'autre recours que de détourner une clé
        // existante — déclarer une météo comme « Bluetooth » pour qu'elle s'affiche.
        // Ce test échouera si la clé disparaît, ce qui est le but.
        Assert.True(IslandSceneCatalog.IsKnown(IslandSceneCatalog.Card));

        IslandFootprint card = IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Card);

        // Elle doit pouvoir contenir un titre, un sous-titre et une rangée de
        // contrôles : plus haute qu'un HUD, et plus large qu'une notification.
        Assert.True(card.Height > IslandSceneCatalog.FootprintFor(IslandSceneCatalog.VolumeHud).Height);
        Assert.True(card.Width > IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Pill).Width);
    }

    [Fact]
    public void Catalog_DeclaredScenesExposeDistinctFootprints()
    {
        // La scène du média est plus grande que la pilule : l'encombrement doit
        // décrire la scène, pas une constante globale.
        Assert.True(
            IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Media).Height
            > IslandSceneCatalog.FootprintFor(IslandSceneCatalog.Pill).Height);
    }
}
