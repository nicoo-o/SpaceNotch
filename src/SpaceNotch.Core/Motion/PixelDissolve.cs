using System;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Fondu en pixels (A2) : entre deux activités, une grille de pixels noirs
/// couvre le contenu en vague (du centre vers les bords), puis se retire sur
/// le nouveau contenu. Chaque case a sa propre fenêtre dans le temps.
/// </summary>
public static class PixelDissolve
{
    /// <summary>Durée totale (couvrir puis découvrir), en secondes.</summary>
    public const double Seconds = 0.36;

    /// <summary>Côté d'une case, en DIPs.</summary>
    public const double CellDip = 6;

    /// <summary>
    /// Opacité du noir sur la case (<paramref name="column"/>, <paramref name="row"/>)
    /// à l'avancement <paramref name="progress"/> (0..1) : monte pendant la
    /// première moitié, redescend pendant la seconde, en retard selon la
    /// distance au centre.
    /// </summary>
    public static double Cover(int column, int row, int columns, int rows, double progress)
    {
        if (columns <= 0 || rows <= 0)
        {
            return 0;
        }

        double cx = (columns - 1) / 2.0, cy = (rows - 1) / 2.0;
        double maxDistance = Math.Sqrt((cx * cx) + (cy * cy));
        double distance = maxDistance <= 0 ? 0 : Math.Sqrt(Math.Pow(column - cx, 2) + Math.Pow(row - cy, 2)) / maxDistance;

        // Fenêtre de chaque case : 40 % de la durée, décalée de 0 à 60 %.
        double delay = distance * 0.3;
        double p = Math.Clamp(progress, 0, 1);
        double coverIn = Math.Clamp((p - delay) / 0.2, 0, 1);
        double coverOut = Math.Clamp((p - 0.5 - delay) / 0.2, 0, 1);

        return Math.Clamp(coverIn - coverOut, 0, 1);
    }

    /// <summary>Le changement de contenu se fait au milieu, quand tout est couvert.</summary>
    public const double SwapAt = 0.5;
}
