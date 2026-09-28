using System;

namespace SpaceNotch.Core.Motion;

/// <summary>Humeur de Pixel, le regard de la notch (P1).</summary>
public enum PixelMood
{
    /// <summary>Éveillé : deux yeux droits qui suivent le curseur.</summary>
    Awake = 0,

    /// <summary>Le curseur survole la notch : les yeux se plissent, contents.</summary>
    Pleased = 1,

    /// <summary>Une notification arrive : les yeux s'arrondissent.</summary>
    Surprised = 2,

    /// <summary>La nuit, ou le PC au repos depuis longtemps : les yeux se ferment.</summary>
    Asleep = 3
}

/// <summary>Forme d'un œil, en pixels de la notch : largeur, hauteur, rondeur (0 carré, 1 cercle).</summary>
public readonly record struct EyeShape(double Width, double Height, double Roundness);

/// <summary>
/// Pixel (P1) : deux yeux de pixels dans la notch au repos, qui suivent le
/// curseur, clignent, se plissent au survol, s'arrondissent à une
/// notification et dorment la nuit.
///
/// <para>
/// Tout est ici, sans Windows : le décalage du regard, la forme de chaque
/// humeur, l'heure du coucher et l'intervalle entre deux clignements, tiré
/// d'une graine pour rester le même d'une exécution à l'autre, donc testable.
/// </para>
/// </summary>
public static class PixelGaze
{
    /// <summary>Déplacement maximal d'un œil vers les côtés, en DIP.</summary>
    public const double MaxLookX = 3;

    /// <summary>Déplacement maximal d'un œil vers le haut ou le bas, en DIP.</summary>
    public const double MaxLookY = 1.5;

    /// <summary>Distance du curseur, en DIP, à laquelle le regard atteint le bord.</summary>
    public const double Reach = 140;

    /// <summary>Durée d'un clignement, en millisecondes.</summary>
    public const int BlinkMilliseconds = 120;

    /// <summary>Heure à laquelle Pixel s'endort.</summary>
    public const int SleepHour = 23;

    /// <summary>Heure à laquelle Pixel se réveille.</summary>
    public const int WakeHour = 7;

    /// <summary>
    /// Décalage des yeux vers le curseur. Le regard suit sans jamais sortir de
    /// l'orbite : il sature doucement, comme un œil qui a atteint le coin.
    /// </summary>
    /// <param name="dx">Position horizontale du curseur par rapport au centre de la notch, en DIP.</param>
    /// <param name="dy">Position verticale du curseur par rapport au centre de la notch, en DIP (vers le bas positif).</param>
    public static (double X, double Y) Look(double dx, double dy)
    {
        if (double.IsNaN(dx) || double.IsNaN(dy))
        {
            return (0, 0);
        }

        return (MaxLookX * Math.Tanh(dx / Reach), MaxLookY * Math.Tanh(dy / Reach));
    }

    /// <summary>Forme des yeux pour une humeur, en DIP.</summary>
    public static EyeShape Shape(PixelMood mood) => mood switch
    {
        PixelMood.Pleased => new EyeShape(10, 4, 0.6),
        PixelMood.Surprised => new EyeShape(11, 11, 1),
        PixelMood.Asleep => new EyeShape(9, 2, 0.2),
        _ => new EyeShape(8, 10, 0.2)
    };

    /// <summary>Forme d'un œil fermé, au milieu d'un clignement.</summary>
    public static EyeShape Blink { get; } = new(8, 1.5, 0.2);

    /// <summary>Vrai pendant les heures de sommeil de Pixel (23 h – 7 h).</summary>
    public static bool IsNight(TimeOnly time) => time.Hour >= SleepHour || time.Hour < WakeHour;

    /// <summary>
    /// Humeur, par ordre de priorité : une notification réveille même la nuit,
    /// le survol vient ensuite, puis le sommeil.
    /// </summary>
    public static PixelMood MoodFor(bool notification, bool hovered, bool idle, TimeOnly time)
    {
        if (notification)
        {
            return PixelMood.Surprised;
        }

        if (hovered)
        {
            return PixelMood.Pleased;
        }

        return idle || IsNight(time) ? PixelMood.Asleep : PixelMood.Awake;
    }

    /// <summary>
    /// Intervalle avant le prochain clignement : entre 2,5 et 6,5 s, tiré de
    /// la graine. Un clignement régulier se lit comme une horloge, pas comme
    /// un regard.
    /// </summary>
    public static TimeSpan NextBlink(int seed)
    {
        uint h = unchecked((uint)seed * 2654435761u);
        h ^= h >> 15;
        h = unchecked(h * 2246822519u);
        h ^= h >> 13;
        double unit = (h % 10_000) / 10_000.0;
        return TimeSpan.FromMilliseconds(2500 + (unit * 4000));
    }
}
