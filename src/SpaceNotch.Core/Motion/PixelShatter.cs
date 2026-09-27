using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>Un éclat : position (DIPs, relative à la zone de texte) et opacité.</summary>
public readonly record struct Shard(double X, double Y, double Opacity);

/// <summary>
/// La recherche éclate en pixels (S3) : effacé par Échap, le texte se brise en
/// carrés qui tombent sous une gravité légère et s'éteignent en 450 ms.
/// Déterministe : les mêmes lettres donnent les mêmes éclats.
/// </summary>
public static class PixelShatter
{
    public const double Seconds = 0.45;
    public const double ShardDip = 3;

    /// <summary>Éclats par lettre.</summary>
    private const int PerLetter = 6;

    private const double Gravity = 260;

    /// <summary>
    /// Éclats à l'instant <paramref name="seconds"/> pour <paramref name="letters"/>
    /// lettres de <paramref name="letterWidth"/> × <paramref name="letterHeight"/> DIPs.
    /// </summary>
    public static IReadOnlyList<Shard> At(int letters, double letterWidth, double letterHeight, double seconds)
    {
        var shards = new List<Shard>();
        double t = seconds / Seconds;

        if (letters <= 0 || t < 0 || t >= 1)
        {
            return shards;
        }

        for (int l = 0; l < Math.Min(letters, 60); l++)
        {
            for (int s = 0; s < PerLetter; s++)
            {
                double r1 = Noise((l * 31) + s), r2 = Noise((l * 17) + (s * 7) + 3), r3 = Noise((l * 13) + (s * 11) + 5);

                // Chaque lettre part un peu après la précédente : la vague va de gauche à droite.
                double local = Math.Clamp((seconds - (l * 0.004)) , 0, Seconds);
                double x0 = (l * letterWidth) + (r1 * letterWidth);
                double y0 = r2 * letterHeight;
                double vx = (r3 - 0.5) * 60;
                double vy = -20 - (r1 * 40);

                double x = x0 + (vx * local);
                double y = y0 + (vy * local) + (0.5 * Gravity * local * local);

                shards.Add(new Shard(
                    Math.Round(x / ShardDip) * ShardDip,
                    Math.Round(y / ShardDip) * ShardDip,
                    Math.Clamp(1 - Math.Pow(t, 1.6), 0, 1)));
            }
        }

        return shards;
    }

    private static double Noise(int n)
    {
        uint h = (uint)n * 2654435761u;
        h ^= h >> 15;
        return (h % 1000) / 1000.0;
    }
}
