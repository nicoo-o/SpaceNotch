namespace SpaceNotch.Core.Motion;

/// <summary>
/// Égaliseur branché au vrai son (F3) : les trois barres de la pastille
/// musique suivent le niveau crête de la sortie audio. Chaque barre a sa
/// propre ondulation, pour que le groupe vive sans se contenter de monter et
/// descendre d'un bloc ; en silence, elles se posent au plancher.
/// </summary>
public static class EqualizerBars
{
    /// <summary>Hauteur minimale, en fraction de la hauteur pleine.</summary>
    public const double Floor = 0.18;

    /// <summary>Niveau sous lequel on considère que le morceau se tait.</summary>
    public const double Silence = 0.02;

    private static readonly double[] Phases = [0.0, 2.1, 4.2];
    private static readonly double[] Speeds = [7.1, 9.3, 5.7];
    private static readonly double[] Weights = [0.85, 1.0, 0.75];

    /// <summary>Hauteurs des trois barres (Floor..1) au niveau <paramref name="level"/> (0..1), à l'instant <paramref name="seconds"/>.</summary>
    public static double[] Heights(double level, double seconds)
    {
        level = Math.Clamp(level, 0, 1);
        var heights = new double[3];

        if (level < Silence)
        {
            Array.Fill(heights, Floor);
            return heights;
        }

        // Un niveau crête est faible pour la plupart des morceaux : la racine le
        // rend lisible sans écraser les passages forts.
        double loud = Math.Sqrt(level);

        for (int i = 0; i < heights.Length; i++)
        {
            double wobble = 0.72 + (0.28 * Math.Sin((seconds * Speeds[i]) + Phases[i]));
            heights[i] = Math.Clamp(Floor + ((1 - Floor) * loud * Weights[i] * wobble), Floor, 1);
        }

        return heights;
    }
}
