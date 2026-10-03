using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Phase B de l'audit d'octobre : physique « Liquide doux », tirer pour
/// ouvrir, appui long, molette et aide des gestes.
/// </summary>
public sealed class PhaseBTests
{
    // ---------------- Physique B ----------------

    [Fact]
    public void Liquide_doux_ouvre_a_046_062_et_ferme_a_038_082()
    {
        SpringParameters open = MotionPresets.Spring(MotionStyle.Natural);
        SpringParameters close = MotionPresets.CloseOf(open);

        Assert.Equal(0.46, open.ResponseSeconds, 3);
        Assert.Equal(0.62, open.DampingRatio, 3);
        Assert.Equal(0.38, close.ResponseSeconds, 3);
        Assert.Equal(0.82, close.DampingRatio, 3);
    }

    [Fact]
    public void La_priorite_rebondit_a_050_et_l_activite_ordinaire_a_080()
    {
        SpringParameters open = MotionPresets.NaturalOpen;

        Assert.Equal(0.50, MotionPresets.ForPriority(open, ActivityPriority.High).DampingRatio, 3);
        Assert.Equal(0.80, MotionPresets.ForPriority(open, ActivityPriority.Normal).DampingRatio, 3);

        // Calme reste calme.
        SpringParameters quiet = MotionPresets.Spring(MotionStyle.Quiet);
        Assert.Same(quiet, MotionPresets.ForPriority(quiet, ActivityPriority.Critical));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(120, 0.35)]
    [InlineData(1200, 3.5)]
    [InlineData(5000, 3.5)]
    [InlineData(-240, -0.7)]
    [InlineData(-5000, -2.1)]
    public void Le_bas_gonfle_selon_la_vitesse(double velocity, double bulge)
        => Assert.Equal(bulge, MotionPresets.Bulge(velocity), 6);

    [Fact]
    public void Le_bombement_ne_sort_pas_de_la_fenetre_et_rejoint_les_conges()
    {
        var geometry = new NotchGeometry();
        var footprint = new IslandFootprint(200, 60);
        const double bulge = 3.5;

        double body = ShapeEffects.BulgeBody(footprint.Height, bulge);
        ShapePoint[] outline = ShapeEffects.Bulged(geometry.Silhouette(new IslandFootprint(200, body)), body, bulge);

        Assert.All(outline, p => Assert.InRange(p.Y, -0.01, footprint.Height + 0.01));
        Assert.Equal(footprint.Height, outline.Max(p => p.Y), 1);

        // Creusé : le bas remonte, sans jamais passer au-dessus du corps moins le creux.
        ShapePoint[] hollow = ShapeEffects.Bulged(geometry.Silhouette(footprint), footprint.Height, -2);
        Assert.True(hollow.Where(p => p.Y > footprint.Height - 2.5).Min(p => p.Y) >= footprint.Height - 2.01);

        // Sans vitesse, rien ne change.
        ShapePoint[] plain = geometry.Silhouette(footprint);
        Assert.Same(plain, ShapeEffects.Bulged(plain, footprint.Height, 0));
    }

    // ---------------- Tirer pour ouvrir ----------------

    [Fact]
    public void Le_seuil_d_ouverture_vaut_max_12_et_03_T()
    {
        Assert.Equal(12, Detachment.PullOpenThreshold(32), 6);
        Assert.Equal(12, Detachment.PullOpenThreshold(40), 6);
        Assert.Equal(18, Detachment.PullOpenThreshold(60), 6);
        Assert.Equal(24, Detachment.PullOpenThreshold(80), 6);

        // T est borné : réglé à 20, il vaut 32.
        Assert.Equal(Detachment.PullOpenThreshold(32), Detachment.PullOpenThreshold(20), 6);
    }

    [Fact]
    public void Lacher_entre_le_seuil_et_T_ouvre_au_dela_detache()
    {
        Assert.False(Detachment.OpensOnRelease(8, 0, 40, allowDetach: true));
        Assert.True(Detachment.OpensOnRelease(12, 0, 40, allowDetach: true));
        Assert.True(Detachment.OpensOnRelease(39, 0, 40, allowDetach: true));
        Assert.False(Detachment.OpensOnRelease(40, 0, 40, allowDetach: true));

        // Sans détachement, toute traction au-delà du seuil ouvre.
        Assert.True(Detachment.OpensOnRelease(200, 0, 40, allowDetach: false));

        // Vers le haut, rien.
        Assert.False(Detachment.OpensOnRelease(-20, 2000, 40, allowDetach: true));
    }

    [Fact]
    public void Un_lancer_vers_le_bas_ouvre_des_6_DIP()
    {
        Assert.True(Detachment.OpensOnRelease(6, 300, 40, allowDetach: true));
        Assert.False(Detachment.OpensOnRelease(5, 900, 40, allowDetach: true));
        Assert.False(Detachment.OpensOnRelease(8, 299, 40, allowDetach: true));
    }

    [Fact]
    public void L_indice_apparait_au_seuil_et_ne_clignote_pas()
    {
        Assert.False(Detachment.ShowsPullHint(11, 40, shown: false));
        Assert.True(Detachment.ShowsPullHint(12, 40, shown: false));

        // Montré, il reste jusqu'à 70 % du seuil.
        Assert.True(Detachment.ShowsPullHint(9, 40, shown: true));
        Assert.False(Detachment.ShowsPullHint(8, 40, shown: true));
    }

    [Fact]
    public void La_vitesse_transmise_suit_l_allongement_et_reste_bornee()
    {
        Assert.Equal(0, Detachment.PullOpenVelocity(20, -500), 6);
        Assert.Equal(0, Detachment.PullOpenVelocity(20, double.NaN), 6);

        // Le tirage résiste : la forme va moins vite que la main.
        double v = Detachment.PullOpenVelocity(20, 800);
        Assert.InRange(v, 1, 800);

        // Mesurée sur l'allongement réel, par différence finie.
        double h = 0.01;
        double slope = (Detachment.PullStretch(20 + h) - Detachment.PullStretch(20 - h)) / (2 * h);
        Assert.Equal(800 * slope, v, 3);

        Assert.Equal(Detachment.PullOpenMaxVelocity, Detachment.PullOpenVelocity(0.1, 100000), 6);
    }

    [Fact]
    public void La_distance_d_arrachement_ne_descend_plus_sous_32()
        => Assert.Equal(32, Detachment.MinimumTearDistance, 6);

    // ---------------- Molette, appui long, aide ----------------

    [Fact]
    public void La_molette_ne_regle_le_volume_qu_avec_une_musique_ou_ouverte()
    {
        Assert.False(NotchGestures.WheelControlsVolume(null, open: false));
        Assert.False(NotchGestures.WheelControlsVolume("feature.timer", open: false));
        Assert.True(NotchGestures.WheelControlsVolume(FeatureKeys.Media, open: false));
        Assert.True(NotchGestures.WheelControlsVolume(FeatureKeys.VolumeHud, open: false));
        Assert.True(NotchGestures.WheelControlsVolume(null, open: true));
    }

    [Fact]
    public void L_appui_long_ne_vaut_qu_au_doigt_ou_au_stylet()
    {
        Assert.True(NotchGestures.IsLongPress(touchOrPen: true, 0.5));
        Assert.False(NotchGestures.IsLongPress(touchOrPen: true, 0.3));
        Assert.False(NotchGestures.IsLongPress(touchOrPen: false, 2));
    }

    [Fact]
    public void L_aide_annonce_tirer_et_ne_promet_plus_le_volume_au_repos()
    {
        var rest = GestureHelp.For(null);

        Assert.Contains(rest, t => t.Gesture.Contains('↓'));
        Assert.DoesNotContain(rest, t => t.Effect == "volume");
        Assert.True(rest.Count <= GestureHelp.MaxTips);
        Assert.Equal(TimeSpan.FromMilliseconds(300), NotchGestures.HelpDelay);
    }
}
