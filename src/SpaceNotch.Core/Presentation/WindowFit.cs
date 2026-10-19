using System;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// Taille d'une fenêtre ordinaire — les Réglages — sur l'écran où elle s'ouvre
/// (n° 29). 920 × 660 DIP ne tiennent pas sur un écran 1366 × 768 à 125 %
/// (zone de travail de 1366 × 708 px, soit 1093 × 566 DIP) : le bas de la
/// fenêtre sortait de l'écran. La taille voulue est gardée quand elle tient,
/// sinon réduite à la zone de travail moins une marge ; le contenu défile.
/// </summary>
public static class WindowFit
{
    /// <summary>
    /// La taille à donner, en pixels physiques comme la zone de travail.
    /// <paramref name="margin"/> est laissée de chaque côté quand il faut réduire.
    /// </summary>
    public static (int Width, int Height) Fit(int width, int height, int areaWidth, int areaHeight, int margin)
        => (Side(width, areaWidth, margin), Side(height, areaHeight, margin));

    private static int Side(int wanted, int area, int margin)
    {
        // Zone inconnue : rien à borner.
        if (area <= 0)
        {
            return wanted;
        }

        int room = area - (2 * margin);

        if (wanted <= room)
        {
            return wanted;
        }

        // Une zone plus étroite que les marges : la fenêtre la remplit.
        return room > 0 ? room : Math.Min(wanted, area);
    }
}
