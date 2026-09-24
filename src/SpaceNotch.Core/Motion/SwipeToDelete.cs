using System;

namespace SpaceNotch.Core.Motion;

/// <summary>Issue d'un balayage de ligne.</summary>
public enum SwipeOutcome
{
    /// <summary>La ligne revient à sa place.</summary>
    Cancel,

    /// <summary>La ligne part : l'élément est supprimé.</summary>
    Delete
}

/// <summary>
/// Balayer une ligne vers la gauche pour la supprimer, comme dans les listes
/// d'iOS : la ligne suit la main, un fond rouge se découvre derrière elle, et
/// le lâcher décide — assez loin, ou assez vite, elle part ; sinon elle revient.
/// Vers la droite, la ligne résiste et ne fait rien.
/// </summary>
public static class SwipeToDelete
{
    /// <summary>Déplacement horizontal au-delà duquel l'appui devient un balayage, en DIPs.</summary>
    public const double StartDistance = 8;

    /// <summary>Part de la largeur à parcourir pour supprimer au lâcher, sans élan.</summary>
    public const double CommitFraction = 0.45;

    /// <summary>Vitesse vers la gauche qui suffit à supprimer, en DIPs par seconde.</summary>
    public const double FlickSpeed = 650;

    /// <summary>Distance minimale d'un geste vif pour qu'il compte : un tressaillement n'efface rien.</summary>
    public const double FlickMinimum = 24;

    /// <summary>Résistance du balayage vers la droite, où il n'y a rien à découvrir.</summary>
    public const double WrongWayDimension = 36;

    /// <summary>
    /// Vrai quand l'appui devient un balayage : assez de chemin horizontal, et
    /// plus horizontal que vertical — sinon c'est la liste qu'on fait défiler.
    /// </summary>
    public static bool Starts(double dx, double dy)
        => Math.Abs(dx) >= StartDistance && Math.Abs(dx) > Math.Abs(dy) * 1.5;

    /// <summary>Décalage affiché de la ligne pour un déplacement de la main.</summary>
    public static double Offset(double dx, double width)
    {
        if (dx >= 0)
        {
            return FluidMotion.RubberBand(dx, WrongWayDimension);
        }

        // Vers la gauche, la ligne suit la main jusqu'à sa largeur, puis résiste.
        double limit = -Math.Max(0, width);

        return dx >= limit ? dx : limit + FluidMotion.RubberBand(dx - limit, WrongWayDimension);
    }

    /// <summary>Découvrement du fond de suppression, de 0 à 1.</summary>
    public static double Reveal(double offset, double width)
        => offset >= 0 || width <= 0 ? 0 : Math.Clamp(-offset / (width * CommitFraction), 0, 1);

    /// <summary>Décision au lâcher.</summary>
    /// <param name="offset">Décalage affiché de la ligne, négatif vers la gauche.</param>
    /// <param name="width">Largeur de la ligne.</param>
    /// <param name="velocity">Vitesse horizontale de la main au lâcher, négative vers la gauche.</param>
    public static SwipeOutcome Decide(double offset, double width, double velocity)
    {
        if (offset >= 0 || width <= 0)
        {
            return SwipeOutcome.Cancel;
        }

        if (-offset >= width * CommitFraction)
        {
            // Même au-delà du seuil, une main qui repart franchement vers la
            // droite annule : c'est ce qu'elle dit.
            return velocity > FlickSpeed ? SwipeOutcome.Cancel : SwipeOutcome.Delete;
        }

        return velocity <= -FlickSpeed && -offset >= FlickMinimum
            ? SwipeOutcome.Delete
            : SwipeOutcome.Cancel;
    }
}
