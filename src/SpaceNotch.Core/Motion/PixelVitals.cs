using System;

namespace SpaceNotch.Core.Motion;

/// <summary>Ce que Pixel laisse voir de la machine.</summary>
public enum PixelCondition
{
    /// <summary>Tout va bien.</summary>
    Normal,

    /// <summary>Batterie faible, sans chargeur : yeux fatigués, regard bas.</summary>
    Tired,

    /// <summary>Processeur au maximum : yeux plissés, une goutte de sueur.</summary>
    Hot,

    /// <summary>Plus de réseau : yeux en croix.</summary>
    Offline
}

/// <summary>
/// Pixel vivant (vague 7) : l'état du PC, la fatigue du soir, le battement de
/// la musique et la teinte qui suit l'heure. Rien de Windows ici : la fenêtre
/// donne les mesures, ce module dit ce que Pixel en fait.
/// </summary>
public static class PixelVitals
{
    /// <summary>Batterie sous laquelle Pixel fatigue, hors charge.</summary>
    public const int LowBattery = 15;

    /// <summary>Charge du processeur (0..1) à partir de laquelle Pixel transpire.</summary>
    public const double HotCpu = 0.9;

    /// <summary>
    /// L'état à montrer, par ordre de gravité : sans réseau d'abord (on ne peut
    /// plus rien recevoir), puis la chaleur, puis la batterie.
    /// </summary>
    public static PixelCondition Condition(int? battery, bool charging, double cpu, bool online)
    {
        if (!online)
        {
            return PixelCondition.Offline;
        }

        if (cpu >= HotCpu)
        {
            return PixelCondition.Hot;
        }

        return battery is { } level && level < LowBattery && !charging ? PixelCondition.Tired : PixelCondition.Normal;
    }

    /// <summary>Forme des yeux pour un état ; null quand l'état n'impose rien (normal, hors ligne dessiné à part).</summary>
    public static EyeShape? Shape(PixelCondition condition) => condition switch
    {
        PixelCondition.Tired => new EyeShape(8, 5, 0.2),
        PixelCondition.Hot => new EyeShape(9, 4, 0.4),
        _ => null
    };

    /// <summary>Regard imposé par l'état : la fatigue baisse les yeux.</summary>
    public static (double X, double Y)? Look(PixelCondition condition)
        => condition == PixelCondition.Tired ? (0, PixelGaze.MaxLookY) : null;

    // ---- Fatigue du soir --------------------------------------------------

    /// <summary>Heure à partir de laquelle Pixel fatigue, avant de dormir à 23 h.</summary>
    public const int EveningHour = 22;

    /// <summary>Vrai entre 22 h et 23 h : paupières mi-closes, bâillements.</summary>
    public static bool IsEvening(TimeOnly time) => time.Hour == EveningHour;

    /// <summary>Les paupières mi-closes du soir.</summary>
    public static EyeShape Drowsy { get; } = new(8, 5.5, 0.3);

    /// <summary>Les yeux serrés d'un bâillement.</summary>
    public static EyeShape Yawn { get; } = new(10, 1.2, 0.2);

    /// <summary>Durée d'un bâillement.</summary>
    public const int YawnMilliseconds = 1100;

    /// <summary>Intervalle avant le prochain bâillement : 40 à 90 s, tiré de la graine.</summary>
    public static TimeSpan NextYawn(int seed)
    {
        uint h = unchecked((uint)seed * 2246822519u);
        h ^= h >> 13;
        h = unchecked(h * 3266489917u);
        h ^= h >> 16;
        return TimeSpan.FromSeconds(40 + ((h % 5000) / 5000.0 * 50));
    }

    // ---- Il danse sur la musique ----------------------------------------------

    /// <summary>Battements par seconde : deux par seconde, un tempo de 120.</summary>
    public const double BeatsPerSecond = 2;

    /// <summary>Niveau sonore sous lequel les yeux restent posés.</summary>
    public const double QuietLevel = 0.02;

    /// <summary>
    /// Le pas de danse à l'instant donné : décalage vertical (DIP, négatif vers
    /// le haut) et écrasement (0 = forme normale, 1 = écrasé au maximum), mis à
    /// l'échelle par le niveau sonore. Un temps sur deux est appuyé.
    /// </summary>
    public static (double Offset, double Squash) Beat(double seconds, double level)
    {
        level = Math.Clamp(level, 0, 1);

        if (level < QuietLevel || double.IsNaN(seconds))
        {
            return (0, 0);
        }

        double phase = seconds * BeatsPerSecond;
        double t = phase - Math.Floor(phase);
        bool strong = ((long)Math.Floor(phase) % 2) == 0;
        double loud = Math.Sqrt(level);

        // Monte vite, retombe, s'écrase un instant sur le temps.
        double lift = t < 0.45 ? Math.Sin(t / 0.45 * Math.PI) : 0;
        double squash = t >= 0.45 && t < 0.7 ? Math.Sin((t - 0.45) / 0.25 * Math.PI) : 0;
        return (-1.6 * lift * loud, (strong ? 1 : 0.5) * squash * loud);
    }

    /// <summary>Forme d'un œil écrasé d'une fraction (0..1) : plus large, moins haut.</summary>
    public static EyeShape Squashed(EyeShape shape, double squash)
    {
        squash = Math.Clamp(squash, 0, 1);
        return shape with { Width = shape.Width * (1 + (0.1 * squash)), Height = shape.Height * (1 - (0.16 * squash)) };
    }

    // ---- Cyan qui suit l'heure ---------------------------------------------

    /// <summary>Le cyan du logo, le jour.</summary>
    public static (byte R, byte G, byte B) Day { get; } = (0x7F, 0xE6, 0xFF);

    /// <summary>L'ambre pâle du soir.</summary>
    public static (byte R, byte G, byte B) Night { get; } = (0xFF, 0xC2, 0x7F);

    /// <summary>
    /// Part du soir dans la teinte (0 = cyan, 1 = ambre) : elle monte de 20 h à
    /// 22 h, reste jusqu'à 7 h, et redescend jusqu'à 8 h. Rien ne change d'un coup.
    /// </summary>
    public static double Warmth(TimeOnly time)
    {
        double h = time.Hour + (time.Minute / 60.0) + (time.Second / 3600.0);

        if (h >= 22 || h < 7)
        {
            return 1;
        }

        if (h >= 20)
        {
            return (h - 20) / 2;
        }

        return h < 8 ? 1 - (h - 7) : 0;
    }

    /// <summary>La teinte de Pixel et des accents à cette heure.</summary>
    public static (byte R, byte G, byte B) Tint(TimeOnly time)
    {
        double w = Warmth(time);
        static byte Mix(byte a, byte b, double t) => (byte)Math.Round(a + ((b - a) * t));
        return (Mix(Day.R, Night.R, w), Mix(Day.G, Night.G, w), Mix(Day.B, Night.B, w));
    }
}
