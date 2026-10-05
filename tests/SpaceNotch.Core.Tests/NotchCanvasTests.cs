using System;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Scenes;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Toile de la notch accrochée en haut (session du 2026-10-04) : une toile qui
/// grandissait à l'ouverture d'une grande scène décalait la notch de ~440 px
/// pendant deux images (rafale à 16 ms, clic sur le repos → recherche).
/// Dimensionnée d'avance sur le plus grand cadre, elle ne grandit plus en
/// pleine transition.
/// </summary>
public sealed class NotchCanvasTests
{
    [Fact]
    public void La_toile_initiale_couvre_le_cadre_mesure_a_l_ouverture_de_la_recherche()
    {
        // Trace [GEO] du 2026-10-04 à 150 % : le cadre a atteint 1 160 × 630 px
        // pendant le dépassement du lanceur ; une toile plus petite grandissait.
        (int width, int height) = NotchCanvas.InitialSize(scale: 1.5, displayWidthPx: 2560, displayHeightPx: 1600);

        Assert.True(width >= 1160, $"largeur {width}");
        Assert.True(height >= 630, $"hauteur {height}");
    }

    [Fact]
    public void La_toile_initiale_couvre_chaque_scene_avec_son_enveloppe_de_mouvement()
    {
        IslandFootprint frame = NotchCanvas.LargestFrame();

        Assert.True(frame.Width >= LauncherLayout.Width * 1.25 * 1.07, $"largeur {frame.Width}");
        Assert.True(frame.Height >= LauncherLayout.MaxHeight * 1.25, $"hauteur {frame.Height}");

        foreach (string key in IslandSceneCatalog.AllKeys)
        {
            IslandFootprint scene = IslandSceneCatalog.FootprintFor(key);
            Assert.True(frame.Width >= scene.Width, key);
            Assert.True(frame.Height >= scene.Height, key);
        }
    }

    [Fact]
    public void La_toile_initiale_ne_depasse_jamais_l_ecran_ni_ne_le_couvre_en_entier()
    {
        // Couvrant exactement le moniteur, la fenêtre au premier plan passerait
        // pour un plein écran (relecture WinUI du 2026-10-04).
        (int width, int height) = NotchCanvas.InitialSize(scale: 2.0, displayWidthPx: 1280, displayHeightPx: 720);

        Assert.Equal(1280, width);
        Assert.Equal(719, height);
    }
}
