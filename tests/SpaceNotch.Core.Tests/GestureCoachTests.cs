using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Gestes enseignés en contexte (ADR-028, volet A) : la rangée d'aide se montre
/// d'elle-même une fois, quand un geste devient utile, et plus jamais une fois
/// le geste utilisé.
/// </summary>
public sealed class GestureCoachTests
{
    private static IslandActivity Scene(string sceneKey) => new()
    {
        Id = "test",
        FeatureId = "test",
        SceneKey = sceneKey,
        Title = "Titre",
        Priority = ActivityPriority.Normal
    };

    [Fact]
    public void La_premiere_musique_enseigne_la_molette_pour_le_volume()
    {
        GestureLesson? lesson = GestureCoach.Next(Scene(IslandSceneCatalog.Media), atRest: true, learned: [], shown: []);

        Assert.NotNull(lesson);
        Assert.Equal(GestureCoach.WheelVolume, lesson.Value.Key);
        Assert.Equal(GestureHelp.For(Scene(IslandSceneCatalog.Media))[0], lesson.Value.Tip);
    }

    [Fact]
    public void Le_presse_papier_enseigne_la_pile_a_la_molette()
        => Assert.Equal(GestureCoach.WheelStack, GestureCoach.Next(Scene(IslandSceneCatalog.Clipboard), atRest: true, learned: [], shown: [])?.Key);

    [Fact]
    public void Un_geste_deja_utilise_ne_s_enseigne_plus()
        => Assert.Null(GestureCoach.Next(Scene(IslandSceneCatalog.Media), atRest: true, learned: [GestureCoach.WheelVolume], shown: []));

    [Fact]
    public void Une_seule_fois_par_session()
        => Assert.Null(GestureCoach.Next(Scene(IslandSceneCatalog.Media), atRest: true, learned: [], shown: [GestureCoach.WheelVolume]));

    [Fact]
    public void Rien_quand_la_notch_est_ouverte_ou_vide()
    {
        Assert.Null(GestureCoach.Next(Scene(IslandSceneCatalog.Media), atRest: false, learned: [], shown: []));
        Assert.Null(GestureCoach.Next(null, atRest: true, learned: [], shown: []));
    }

    [Fact]
    public void Rien_pour_une_activite_sans_geste_a_enseigner()
        => Assert.Null(GestureCoach.Next(Scene(IslandSceneCatalog.Notification), atRest: true, learned: [], shown: []));
}
