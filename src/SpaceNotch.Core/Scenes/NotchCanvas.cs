using System;
using System.Linq;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Motion;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Taille de départ de la toile de la notch accrochée en haut.
///
/// <para>
/// La toile est une fenêtre qui ne fait que grandir, pour que les transitions
/// ne la redimensionnent pas : la surface XAML suit un redimensionnement avec
/// une ou deux images de retard. Partie de la forme du repos, elle grandissait
/// pourtant pendant l'ouverture d'une grande scène — et la notch était peinte
/// décalée de la moitié de l'agrandissement (~440 px à l'ouverture de la
/// recherche, rafale et trace [GEO] du 2026-10-04). Elle part donc d'emblée du
/// plus grand cadre qu'une transition puisse demander.
/// </para>
/// </summary>
public static class NotchCanvas
{
    /// <summary>Dépassement de la forme au-delà de sa cible (physique « Liquide doux » : ~8 %).</summary>
    public const double OvershootShare = 0.10;

    /// <summary>
    /// Le plus grand cadre de fenêtre qu'une transition du répertoire puisse
    /// demander, en DIPs : la plus grande scène (le lanceur à sa hauteur
    /// maximale), avec son dépassement, la marge de l'enveloppe et, en largeur,
    /// l'écrasement maximal. Une scène de greffon plus grande, ou une forme
    /// étirée par le geste, fait encore grandir la toile en mouvement.
    /// </summary>
    public static IslandFootprint LargestFrame()
    {
        double width = Math.Max(LauncherLayout.Width, IslandSceneCatalog.AllKeys.Max(k => IslandSceneCatalog.FootprintFor(k).Width));
        double height = Math.Max(LauncherLayout.MaxHeight, IslandSceneCatalog.AllKeys.Max(k => IslandSceneCatalog.FootprintFor(k).Height));
        // L'enveloppe est comptée sur toute la taille, et non sur le seul trajet.
        double growth = 1 + OvershootShare + MotionPresets.EnvelopeShare;

        return new IslandFootprint(
            Math.Ceiling(width * growth * (1 + MotionPresets.MaxSquash)),
            Math.Ceiling(height * growth));
    }

    /// <summary>
    /// Taille de départ de la toile, en pixels physiques, bornée par l'écran —
    /// et d'un pixel plus basse que lui : une fenêtre au premier plan qui couvre
    /// exactement le moniteur passe pour un plein écran aux yeux du shell, et la
    /// notch se masquerait elle-même pendant la frappe.
    /// </summary>
    public static (int Width, int Height) InitialSize(double scale, int displayWidthPx, int displayHeightPx)
    {
        IslandFootprint frame = LargestFrame();
        int width = (int)Math.Ceiling(frame.Width * scale);
        int height = (int)Math.Ceiling(frame.Height * scale);

        return (Math.Min(width, Math.Max(1, displayWidthPx)), Math.Min(height, Math.Max(1, displayHeightPx - 1)));
    }
}
