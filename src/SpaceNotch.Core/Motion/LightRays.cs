using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>Un pixel de rayon : position relative au bas de la notch (DIPs) et opacité.</summary>
public readonly record struct RayPixel(double X, double Y, double Opacity);

/// <summary>
/// Rayons de lumière en pixels (M4) : pour les vraies réussites, neuf rayons de
/// pixels partent du bas de la notch pendant 0,6 s puis tout redevient noir.
/// </summary>
public static class LightRays
{
    public const int Rays = 9;

    /// <summary>
    /// Vraie réussite à fêter ? Une activité qui passe à « terminé » — fichier
    /// téléchargé, minuteur ou focus achevé. Une activité déjà terminée qu'on
    /// republie ne rejoue rien : c'est rare, donc ça reste un plaisir.
    /// </summary>
    public static bool Celebrates(SpaceNotch.Core.Activities.IslandActivity? before, SpaceNotch.Core.Activities.IslandActivity? after)
    {
        static bool Done(SpaceNotch.Core.Activities.IslandActivity? a)
            => a is { MotionState: ActivityMotionState.Completing or ActivityMotionState.Complete };

        return Done(after) && !(Done(before) && string.Equals(before!.Id, after!.Id, StringComparison.Ordinal));
    }
    public const double Seconds = 0.6;
    public const double PixelDip = 2;

    /// <summary>Pixels allumés par rayon, de la source vers la pointe.</summary>
    private const int Trail = 5;

    /// <summary>Portée maximale d'un rayon, en DIPs.</summary>
    private const double Reach = 28;

    /// <summary>
    /// Pixels à l'instant <paramref name="seconds"/>, pour une notch large de
    /// <paramref name="width"/> DIPs. Origine : le milieu de son bord bas. Les
    /// rayons s'ouvrent en éventail vers le bas, sur 140°.
    /// </summary>
    public static IReadOnlyList<RayPixel> At(double seconds, double width)
    {
        var pixels = new List<RayPixel>();
        double t = seconds / Seconds;

        if (t <= 0 || t >= 1)
        {
            return pixels;
        }

        double head = Reach * (1 - Math.Pow(1 - t, 2));
        double fade = t < 0.7 ? 1 : 1 - ((t - 0.7) / 0.3);

        for (int r = 0; r < Rays; r++)
        {
            double angle = (20 + (140.0 * r / (Rays - 1))) * Math.PI / 180;
            double originX = width / 2 * Math.Cos(angle) * 0.6;

            for (int p = 0; p < Trail; p++)
            {
                double distance = head - (p * PixelDip * 2.5);

                if (distance <= 0)
                {
                    break;
                }

                double x = originX + (Math.Cos(angle) * distance);
                double y = Math.Sin(angle) * distance;

                // Calé sur la grille des pixels de 2 DIPs : pas de demi-pixel flou.
                pixels.Add(new RayPixel(
                    Math.Round(x / PixelDip) * PixelDip,
                    Math.Round(y / PixelDip) * PixelDip,
                    fade * (1 - (p / (double)Trail))));
            }
        }

        return pixels;
    }
}
