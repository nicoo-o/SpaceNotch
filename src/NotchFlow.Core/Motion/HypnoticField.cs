using System;
using System.Collections.Generic;

namespace NotchFlow.Core.Motion;

/// <summary>
/// Une particule lumineuse du champ hypnotique.
/// </summary>
/// <param name="X">Abscisse relative au centre, de −1 (bord gauche) à 1 (bord droit).</param>
/// <param name="Y">Ordonnée relative au centre, de −1 (haut) à 1 (bas).</param>
/// <param name="Scale">Taille relative, 1 étant la taille de référence d'une particule.</param>
/// <param name="Intensity">Luminosité, de 0 (éteinte) à 1.</param>
public readonly record struct HypnoticMote(double X, double Y, double Scale, double Intensity);

/// <summary>
/// Une image du champ hypnotique : une source, son halo, quelques particules, et
/// l'impulsion transmise à l'atmosphère.
/// </summary>
/// <param name="CoreScale">Taille de la source lumineuse, 1 au repos.</param>
/// <param name="CoreIntensity">Luminosité de la source, de 0 à 1.</param>
/// <param name="CoreOffsetX">Décalage horizontal de la source, relatif (micro-secousse, penchant).</param>
/// <param name="HaloScale">Taille du halo, 1 au repos.</param>
/// <param name="HaloIntensity">Luminosité du halo, de 0 à 1.</param>
/// <param name="Motes">Les particules, toujours au nombre de <see cref="HypnoticField.MoteCount"/>.</param>
/// <param name="AmbientPulse">
/// Impulsion transmise à l'atmosphère, de 0 à 1 : c'est ce qui fait respirer la
/// dissolution sous la notch au même rythme que la matière qui travaille.
/// </param>
public sealed record HypnoticFrame(
    double CoreScale,
    double CoreIntensity,
    double CoreOffsetX,
    double HaloScale,
    double HaloIntensity,
    IReadOnlyList<HypnoticMote> Motes,
    double AmbientPulse);

/// <summary>
/// Le champ hypnotique, décrit comme une fonction pure du temps.
///
/// <para>
/// <b>Pourquoi une fonction pure.</b> Le rendu n'évalue pas ce champ à chaque
/// image : il en échantillonne une boucle, la confie au compositeur sous forme
/// d'images clés, et le GPU la rejoue seul. Le fil d'interface ne fait rien
/// pendant que la matière bouge, et le moteur s'arrête entièrement au repos.
/// La contrepartie est que chaque préréglage en boucle doit être exactement
/// périodique — ce que les tests vérifient.
/// </para>
///
/// <para>
/// <b>Pourquoi si peu de primitives.</b> Une source, un halo, quatre particules.
/// L'effet hypnotique ne vient pas du nombre mais du rythme : respiration,
/// flux, attraction, dispersion, convergence. Un vrai système de particules
/// coûterait un rendu permanent pour un gain que l'œil ne mesure pas à cette
/// taille.
/// </para>
/// </summary>
public static class HypnoticField
{
    /// <summary>Nombre de particules du champ.</summary>
    public const int MoteCount = 4;

    /// <summary>
    /// Échantillons par boucle confiés au compositeur. Trente-deux suffisent :
    /// entre deux échantillons l'interpolation est linéaire, et à cette taille
    /// l'écart à la courbe reste sous le pixel.
    /// </summary>
    public const int DefaultSamples = 32;

    private const double Tau = 2 * Math.PI;

    /// <summary>Vrai pour les préréglages qui bouclent tant que le travail dure.</summary>
    public static bool IsLooping(HypnoticPreset preset)
        => preset is not (HypnoticPreset.None or HypnoticPreset.Complete or HypnoticPreset.Error);

    /// <summary>
    /// Durée d'une boucle, ou de l'unique passage d'un préréglage ponctuel, en
    /// secondes. Le rythme porte le sens : la lecture respire, le traitement bat.
    /// </summary>
    public static double PeriodSeconds(HypnoticPreset preset) => preset switch
    {
        HypnoticPreset.Read => 3.6,
        HypnoticPreset.Think => 4.8,
        HypnoticPreset.Search => 1.8,
        HypnoticPreset.Process => 1.4,
        HypnoticPreset.Sync => 2.4,
        HypnoticPreset.Drop => 1.2,
        HypnoticPreset.Complete => 0.9,
        HypnoticPreset.Error => 0.7,
        _ => 0
    };

    /// <summary>
    /// Préréglage effectif d'une activité, d'après son état de travail et le
    /// préréglage qu'elle déclare.
    ///
    /// L'état décide s'il y a mouvement ; le préréglage déclaré décide lequel.
    /// Une activité qui ne déclare rien reçoit le mouvement générique de son
    /// état, ce qui permet à une fonctionnalité de n'écrire qu'une ligne.
    /// </summary>
    public static HypnoticPreset Resolve(ActivityMotionState state, HypnoticPreset declared) => state switch
    {
        ActivityMotionState.Completing => HypnoticPreset.Complete,
        ActivityMotionState.Error => HypnoticPreset.Error,
        ActivityMotionState.Attention => IsLooping(declared) ? declared : HypnoticPreset.Read,
        ActivityMotionState.Working => IsLooping(declared) ? declared : HypnoticPreset.Process,
        _ => HypnoticPreset.None
    };

    /// <summary>Image de repos : une source douce, sans particule, sans impulsion.</summary>
    public static HypnoticFrame Rest { get; } = new(
        CoreScale: 1,
        CoreIntensity: 0.55,
        CoreOffsetX: 0,
        HaloScale: 1,
        HaloIntensity: 0.2,
        Motes: [new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0), new(0, 0, 0, 0)],
        AmbientPulse: 0);

    /// <summary>
    /// Image fixe représentative, pour la réduction des animations : la même
    /// composition, sans le mouvement. L'information reste, l'animation part.
    /// </summary>
    public static HypnoticFrame StaticFrame(HypnoticPreset preset)
    {
        if (preset == HypnoticPreset.None)
        {
            return Rest;
        }

        double period = PeriodSeconds(preset);

        // Un préréglage ponctuel se fige sur son dernier état ; une boucle, sur
        // un instant où ses particules sont réparties et visibles.
        return Evaluate(preset, IsLooping(preset) ? period * 0.3 : period);
    }

    /// <summary>
    /// Évalue le champ à l'instant <paramref name="seconds"/> depuis le début du
    /// préréglage. Les boucles se répètent ; les préréglages ponctuels restent
    /// figés sur leur dernière image une fois leur durée écoulée.
    /// </summary>
    public static HypnoticFrame Evaluate(HypnoticPreset preset, double seconds)
    {
        double period = PeriodSeconds(preset);

        if (period <= 0 || double.IsNaN(seconds) || double.IsInfinity(seconds))
        {
            return Rest;
        }

        double t = Math.Max(0, seconds);

        if (IsLooping(preset))
        {
            double p = Fraction(t / period);

            return preset switch
            {
                HypnoticPreset.Read => Read(p),
                HypnoticPreset.Think => Think(p),
                HypnoticPreset.Search => Search(p),
                HypnoticPreset.Process => Process(p),
                HypnoticPreset.Sync => Sync(p),
                HypnoticPreset.Drop => Drop(p),
                _ => Rest
            };
        }

        double u = Math.Clamp(t / period, 0, 1);

        return preset == HypnoticPreset.Complete ? Complete(u) : Error(u);
    }

    /// <summary>
    /// Échantillonne une boucle — ou l'unique passage d'un préréglage ponctuel —
    /// en <paramref name="samples"/> + 1 images réparties de 0 à 1 inclus.
    ///
    /// Pour une boucle, la dernière image est celle du départ : c'est ce qui rend
    /// la reprise invisible quand le compositeur recommence l'animation.
    /// </summary>
    public static IReadOnlyList<(double Progress, HypnoticFrame Frame)> Sample(
        HypnoticPreset preset,
        int samples = DefaultSamples)
    {
        int count = Math.Max(2, samples);
        double period = PeriodSeconds(preset);
        var frames = new List<(double, HypnoticFrame)>(count + 1);

        for (int i = 0; i <= count; i++)
        {
            double progress = (double)i / count;

            HypnoticFrame frame = i == count && IsLooping(preset)
                ? Evaluate(preset, 0)
                : Evaluate(preset, progress * period);

            frames.Add((progress, frame));
        }

        return frames;
    }

    // ------------------------------------------------------------------
    // Préréglages en boucle — p ∈ [0, 1[
    // ------------------------------------------------------------------

    /// <summary>Lecture : une respiration, une orbite lente et aplatie comme une ligne qu'on parcourt.</summary>
    private static HypnoticFrame Read(double p)
    {
        double breath = Breath(p);
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double angle = Tau * (p + ((double)i / MoteCount));
            motes[i] = new HypnoticMote(
                0.62 * Math.Cos(angle),
                0.34 * Math.Sin(angle),
                0.35,
                0.30 + (0.25 * breath));
        }

        return new HypnoticFrame(
            CoreScale: 0.90 + (0.12 * breath),
            CoreIntensity: 0.60 + (0.25 * breath),
            CoreOffsetX: 0,
            HaloScale: 1.0 + (0.20 * breath),
            HaloIntensity: 0.25 + (0.20 * breath),
            Motes: motes,
            AmbientPulse: 0.30 + (0.40 * breath));
    }

    /// <summary>
    /// Réflexion : des trajectoires de Lissajous à fréquences entières — donc
    /// organiques à l'œil, mais exactement périodiques.
    /// </summary>
    private static HypnoticFrame Think(double p)
    {
        ReadOnlySpan<int> ax = [1, 2, 1, 3];
        ReadOnlySpan<int> ay = [2, 3, 3, 2];

        double breath = Math.Clamp(0.5 - (0.3 * Math.Cos(Tau * p)) - (0.2 * Math.Cos((2 * Tau * p) + 1)), 0, 1);
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double phase = i * 1.7;
            motes[i] = new HypnoticMote(
                0.70 * Math.Sin((Tau * ax[i] * p) + phase),
                0.45 * Math.Sin((Tau * ay[i] * p) + (phase * 0.6)),
                0.30 + (0.15 * (0.5 + (0.5 * Math.Sin((2 * Tau * p) + i)))),
                0.30 + (0.35 * (0.5 + (0.5 * Math.Sin((Tau * p) + phase)))));
        }

        return new HypnoticFrame(
            CoreScale: 0.92 + (0.14 * breath),
            CoreIntensity: 0.58 + (0.30 * breath),
            CoreOffsetX: 0.04 * Math.Sin(Tau * p),
            HaloScale: 1.0 + (0.25 * breath),
            HaloIntensity: 0.25 + (0.25 * breath),
            Motes: motes,
            AmbientPulse: 0.35 + (0.40 * breath));
    }

    /// <summary>
    /// Recherche : un balayage de gauche à droite. Les particules naissent et
    /// meurent éteintes, si bien que leur retour au bord gauche n'est jamais vu.
    /// </summary>
    private static HypnoticFrame Search(double p)
    {
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double q = Fraction(p + ((double)i / MoteCount));
            motes[i] = new HypnoticMote(
                -0.9 + (1.8 * q),
                0.15 * Math.Sin(Tau * q),
                0.28 + (0.12 * Math.Sin(Math.PI * q)),
                0.75 * Math.Sin(Math.PI * q));
        }

        double beat = 0.5 - (0.5 * Math.Cos(Tau * 4 * p));

        return new HypnoticFrame(
            CoreScale: 0.95 + (0.08 * beat),
            CoreIntensity: 0.55 + (0.30 * beat),
            CoreOffsetX: 0.08 * Math.Sin(Tau * p),
            HaloScale: 1.05 + (0.10 * beat),
            HaloIntensity: 0.28 + (0.15 * beat),
            Motes: motes,
            AmbientPulse: 0.35 + (0.30 * beat));
    }

    /// <summary>Traitement : une orbite rapide et serrée dont le rayon bat — dense, énergique.</summary>
    private static HypnoticFrame Process(double p)
    {
        double beat = 0.5 - (0.5 * Math.Cos(Tau * 2 * p));
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double angle = Tau * (p + ((double)i / MoteCount));
            double radius = 0.55 + (0.15 * Math.Sin((Tau * 2 * p) + i));
            motes[i] = new HypnoticMote(
                radius * Math.Cos(angle),
                radius * 0.7 * Math.Sin(angle),
                0.32,
                0.55 + (0.30 * (0.5 + (0.5 * Math.Sin((Tau * 2 * p) + i)))));
        }

        return new HypnoticFrame(
            CoreScale: 1.0 + (0.08 * beat),
            CoreIntensity: 0.70 + (0.25 * beat),
            CoreOffsetX: 0,
            HaloScale: 1.10 + (0.15 * beat),
            HaloIntensity: 0.35 + (0.20 * beat),
            Motes: motes,
            AmbientPulse: 0.50 + (0.35 * beat));
    }

    /// <summary>Synchronisation : un flux qui va et vient d'un pôle à l'autre.</summary>
    private static HypnoticFrame Sync(double p)
    {
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double phase = i * Math.PI / 4;
            motes[i] = new HypnoticMote(
                0.80 * Math.Cos((Tau * p) + phase),
                0.18 * Math.Sin((2 * Tau * p) + i),
                0.30,
                0.40 + (0.30 * Math.Abs(Math.Cos((Tau * p) + phase))));
        }

        double beat = 0.5 + (0.5 * Math.Cos(2 * Tau * p));

        return new HypnoticFrame(
            CoreScale: 0.95 + (0.06 * beat),
            CoreIntensity: 0.60 + (0.20 * beat),
            CoreOffsetX: 0,
            HaloScale: 1.05 + (0.10 * beat),
            HaloIntensity: 0.28 + (0.14 * beat),
            Motes: motes,
            AmbientPulse: 0.35 + (0.25 * beat));
    }

    /// <summary>
    /// Dépôt : les particules viennent du bord et sont absorbées par la source,
    /// en accélérant. Elles naissent et disparaissent éteintes.
    /// </summary>
    private static HypnoticFrame Drop(double p)
    {
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double q = Fraction(p + ((double)i / MoteCount));
            double radius = Math.Pow(1 - q, 1.4);
            double angle = (i * Math.PI / 2) + (Math.PI / 4);
            motes[i] = new HypnoticMote(
                radius * 0.9 * Math.Cos(angle),
                radius * 0.9 * Math.Sin(angle),
                0.20 + (0.25 * (1 - q)),
                0.80 * Math.Sin(Math.PI * q));
        }

        double absorb = 0.5 - (0.5 * Math.Cos(Tau * 4 * p));

        return new HypnoticFrame(
            CoreScale: 1.0 + (0.10 * absorb),
            CoreIntensity: 0.60 + (0.25 * absorb),
            CoreOffsetX: 0,
            HaloScale: 1.10 + (0.15 * absorb),
            HaloIntensity: 0.35 + (0.20 * absorb),
            Motes: motes,
            AmbientPulse: 0.45 + (0.30 * absorb));
    }

    // ------------------------------------------------------------------
    // Préréglages ponctuels — u ∈ [0, 1]
    // ------------------------------------------------------------------

    /// <summary>Achèvement : convergence, impulsion lumineuse, puis retour au calme.</summary>
    private static HypnoticFrame Complete(double u)
    {
        double converge = Math.Clamp(u / 0.45, 0, 1);
        double radius = 0.6 * (1 - (converge * converge));
        double pulse = Math.Exp(-Math.Pow((u - 0.5) / 0.16, 2));
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double angle = (i * Math.PI / 2) + (Math.PI / 4);
            motes[i] = new HypnoticMote(
                radius * Math.Cos(angle),
                radius * Math.Sin(angle),
                0.30,
                0.80 * (1 - converge));
        }

        return new HypnoticFrame(
            CoreScale: 1.0 + (0.45 * pulse),
            CoreIntensity: 0.70 + (0.30 * pulse) - (0.10 * u),
            CoreOffsetX: 0,
            HaloScale: 1.0 + (0.60 * pulse) + (0.10 * u),
            HaloIntensity: 0.30 + (0.40 * pulse) - (0.05 * u),
            Motes: motes,
            AmbientPulse: Math.Clamp(0.30 + (0.70 * pulse), 0, 1));
    }

    /// <summary>Échec : dispersion vers l'extérieur, micro-secousse amortie, extinction.</summary>
    private static HypnoticFrame Error(double u)
    {
        double spread = 1 - Math.Pow(1 - u, 2);
        double radius = 0.3 + (0.7 * spread);
        var motes = new HypnoticMote[MoteCount];

        for (int i = 0; i < MoteCount; i++)
        {
            double angle = (i * Math.PI / 2) + (Math.PI / 4);
            motes[i] = new HypnoticMote(
                radius * Math.Cos(angle),
                radius * 0.8 * Math.Sin(angle),
                0.30,
                0.70 * (1 - u));
        }

        return new HypnoticFrame(
            CoreScale: 1.0 - (0.10 * spread),
            CoreIntensity: 0.70 - (0.25 * u),
            CoreOffsetX: 0.12 * Math.Sin(Tau * 3 * u) * (1 - u),
            HaloScale: 1.0,
            HaloIntensity: 0.30 - (0.15 * u),
            Motes: motes,
            AmbientPulse: 0.40 - (0.25 * u));
    }

    // ------------------------------------------------------------------

    /// <summary>Respiration : 0 → 1 → 0 sur une période, sans coude.</summary>
    private static double Breath(double p) => 0.5 - (0.5 * Math.Cos(Tau * p));

    private static double Fraction(double value) => value - Math.Floor(value);
}
