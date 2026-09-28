using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Clic d'encre (A5) : à l'appui dans la notch, un anneau de pixels part du
/// point de contact et s'éteint. Un retour « haptique » visuel, tracé comme un
/// cercle de Bresenham : des pixels nets, jamais un cercle lissé.
/// </summary>
public static class InkRing
{
    /// <summary>Rayon final de l'anneau, en pixels de la notch.</summary>
    public const int MaxRadius = 13;

    /// <summary>Pas entre deux pixels, en DIP.</summary>
    public const double Pitch = 2;

    /// <summary>Durée totale, en millisecondes.</summary>
    public const int DurationMilliseconds = 180;

    /// <summary>Nombre d'images de l'expansion.</summary>
    public const int Frames = 6;

    /// <summary>Pixels d'un cercle de rayon donné (algorithme du point milieu), sans doublon.</summary>
    public static IReadOnlyList<(int X, int Y)> Circle(int radius)
    {
        if (radius <= 0)
        {
            return [(0, 0)];
        }

        var set = new HashSet<(int, int)>();
        int x = radius, y = 0, error = 1 - radius;

        while (x >= y)
        {
            foreach ((int X, int Y) p in new[] { (x, y), (y, x), (-y, x), (-x, y), (-x, -y), (-y, -x), (y, -x), (x, -y) })
            {
                set.Add(p);
            }

            y++;

            if (error < 0)
            {
                error += (2 * y) + 1;
            }
            else
            {
                x--;
                error += 2 * (y - x) + 1;
            }
        }

        var list = new List<(int X, int Y)>(set);
        list.Sort((a, b) => Math.Atan2(a.Y, a.X).CompareTo(Math.Atan2(b.Y, b.X)));
        return list;
    }

    /// <summary>Rayon de l'image <paramref name="frame"/> (0 à <see cref="Frames"/> − 1) : l'anneau ralentit en s'élargissant.</summary>
    public static int RadiusAt(int frame)
    {
        double t = Math.Clamp((frame + 1) / (double)Frames, 0, 1);
        return Math.Max(1, (int)Math.Round(MaxRadius * (1 - Math.Pow(1 - t, 2))));
    }

    /// <summary>Opacité de l'image <paramref name="frame"/> : l'anneau s'éteint en s'élargissant.</summary>
    public static double OpacityAt(int frame) => Math.Clamp(1 - (frame / (double)Frames), 0, 1);
}
