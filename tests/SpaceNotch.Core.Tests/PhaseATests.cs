using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Presentation;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Phase A de l'audit d'octobre : le Narrateur ne réannonce plus un minuteur
/// chaque seconde, et un clic qui a un peu glissé reste un clic.
/// </summary>
public sealed class PhaseATests
{
    private static IslandActivity Activity(string id, string title)
        => new() { Id = id, FeatureId = "test", SceneKey = "info", Title = title };

    [Fact]
    public void Un_minuteur_qui_defile_garde_la_meme_cle_d_annonce()
    {
        Assert.Equal(Announcement.Key(Activity("timer", "12:34")), Announcement.Key(Activity("timer", "12:33")));
        Assert.Equal(Announcement.Key(Activity("dl", "Téléchargement 45 %")), Announcement.Key(Activity("dl", "Téléchargement 46 %")));
    }

    [Fact]
    public void Un_vrai_changement_de_titre_reste_annonce()
    {
        Assert.NotEqual(Announcement.Key(Activity("timer", "00:01")), Announcement.Key(Activity("timer", "Temps écoulé")));
        Assert.NotEqual(Announcement.Key(Activity("dl", "Téléchargement 99 %")), Announcement.Key(Activity("dl", "Téléchargé")));
        Assert.NotEqual(Announcement.Key(Activity("a", "Bonjour")), Announcement.Key(Activity("b", "Bonjour")));
        Assert.Equal(string.Empty, Announcement.Key(null));
    }

    [Theory]
    [InlineData(0, 0, true)]
    [InlineData(5, 0, true)]      // un peu vers le bas
    [InlineData(1, 10, true)]     // dérive latérale du pavé tactile
    [InlineData(-3, 11, true)]    // un peu vers le haut et de côté
    [InlineData(6, 0, false)]     // une vraie traction
    [InlineData(2, 12, false)]    // un vrai glissé de côté
    [InlineData(30, 0, false)]
    public void Un_clic_qui_glisse_un_peu_reste_un_clic(double pull, double lateral, bool click)
        => Assert.Equal(click, Detachment.IsClickRelease(pull, lateral));
}
