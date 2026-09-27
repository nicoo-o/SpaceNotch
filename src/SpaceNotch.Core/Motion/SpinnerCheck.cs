using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Le spinner devient une coche (M2) : sept pixels tournent en anneau pendant
/// le travail ; à la fin, ils ne s'éteignent pas, ils glissent jusqu'à la forme
/// d'une coche. Coordonnées dans une grille de 7 × 7, centre (3, 3).
/// </summary>
public static class SpinnerCheck
{
    /// <summary>Nombre de pixels de l'anneau et de la coche.</summary>
    public const int Count = 7;

    /// <summary>Durée de la migration vers la coche, en secondes.</summary>
    public const double MorphSeconds = 0.38;

    /// <summary>Tour complet de l'anneau, en secondes.</summary>
    public const double TurnSeconds = 1.1;

    private const double Radius = 2.6;

    /// <summary>La coche, du bas du petit trait au bout du grand.</summary>
    private static readonly (double X, double Y)[] Check =
    [
        (0.5, 3.5), (1.5, 4.5), (2.5, 5.5), (3.5, 4.5), (4.5, 3.5), (5.5, 2.5), (6.5, 1.5)
    ];

    /// <summary>Position des pixels de l'anneau, <paramref name="seconds"/> après le départ.</summary>
    public static IReadOnlyList<(double X, double Y)> Ring(double seconds)
    {
        var points = new (double, double)[Count];
        double turn = seconds / TurnSeconds * 2 * Math.PI;

        for (int i = 0; i < Count; i++)
        {
            double a = turn + (i * 2 * Math.PI / Count);
            points[i] = (3.5 + (Radius * Math.Cos(a)), 3.5 + (Radius * Math.Sin(a)));
        }

        return points;
    }

    /// <summary>
    /// Pixels pendant la migration : de l'anneau figé à <paramref name="ringSeconds"/>
    /// jusqu'à la coche, <paramref name="progress"/> allant de 0 à 1 (adouci).
    /// </summary>
    public static IReadOnlyList<(double X, double Y)> Morph(double ringSeconds, double progress)
    {
        IReadOnlyList<(double X, double Y)> ring = Ring(ringSeconds);
        double t = Ease(Math.Clamp(progress, 0, 1));
        var points = new (double, double)[Count];

        // Chaque pixel de l'anneau va vers la case de la coche la plus proche
        // dans l'ordre angulaire : aucun croisement de trajectoires.
        int start = NearestToCheckStart(ring);

        for (int i = 0; i < Count; i++)
        {
            (double X, double Y) from = ring[(start + i) % Count];
            (double X, double Y) to = Check[i];
            points[i] = (from.X + ((to.X - from.X) * t), from.Y + ((to.Y - from.Y) * t));
        }

        return points;
    }

    /// <summary>Forme finale.</summary>
    public static IReadOnlyList<(double X, double Y)> Done() => Check;

    private static int NearestToCheckStart(IReadOnlyList<(double X, double Y)> ring)
    {
        int best = 0;
        double bestDistance = double.MaxValue;

        for (int i = 0; i < ring.Count; i++)
        {
            double d = Math.Pow(ring[i].X - Check[0].X, 2) + Math.Pow(ring[i].Y - Check[0].Y, 2);

            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return best;
    }

    private static double Ease(double t) => 1 - Math.Pow(1 - t, 3);
}
