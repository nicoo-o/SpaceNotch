using System;
using System.Collections.Generic;
using System.Linq;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Couleur de la matière hypnotique, neutre vis-à-vis de toute bibliothèque
/// d'interface.
/// </summary>
public readonly record struct HypnoticColor(byte R, byte G, byte B)
{
    /// <summary>Interpolation linéaire, composante par composante.</summary>
    public static HypnoticColor Lerp(HypnoticColor from, HypnoticColor to, double t)
    {
        double k = Math.Clamp(t, 0, 1);

        return new HypnoticColor(
            (byte)Math.Round(from.R + ((to.R - from.R) * k)),
            (byte)Math.Round(from.G + ((to.G - from.G) * k)),
            (byte)Math.Round(from.B + ((to.B - from.B) * k)));
    }
}

/// <summary>
/// Une image du champ hypnotique : neuf pixels, leur couleur, leur halo.
/// </summary>
/// <param name="Cells">
/// Intensité des neuf pixels de la grille 3 × 3, de 0 (éteint) à 1, ligne par
/// ligne depuis le coin supérieur gauche.
/// </param>
/// <param name="Color">Couleur commune des pixels et de leur halo.</param>
/// <param name="Bloom">Intensité du halo lumineux autour de la grille, de 0 à 1.</param>
/// <param name="ShakeX">Décalage horizontal de la grille, relatif à sa largeur (micro-secousse d'erreur).</param>
/// <param name="AmbientPulse">
/// Impulsion transmise à l'atmosphère, de 0 à 1 : la dissolution sous la notch
/// respire au rythme de la lumière de la grille.
/// </param>
public sealed record HypnoticFrame(
    IReadOnlyList<double> Cells,
    HypnoticColor Color,
    double Bloom,
    double ShakeX,
    double AmbientPulse);

/// <summary>
/// Le champ hypnotique : une grille de 3 × 3 pixels lumineux, dont le motif
/// change par pas courts avec un fondu, et dont la couleur dérive lentement.
///
/// <para>
/// <b>D'après la référence.</b> La vidéo « Hypnotizing UI » d'Inspora montre une
/// petite matrice de pixels carrés, collés les uns aux autres, avec un halo
/// doux : les motifs — croix, anneau, losange, plein, coins, plus — se
/// succèdent toutes les 150 à 250 ms en se fondant, et la teinte dérive selon
/// l'état (bleu pour la lecture, orange puis corail pour la réflexion, pêche,
/// rose, bleu puis lavande pendant la construction). C'est cette grammaire qui
/// est reprise ici, détachée de l'IA : chaque préréglage signifie une nature de
/// travail. Voir ADR-018.
/// </para>
///
/// <para>
/// <b>Une fonction exactement cuisable.</b> Chaque grandeur est linéaire par
/// morceaux dans le temps. <see cref="Keyframes"/> renvoie exactement les
/// points de rupture : le compositeur, qui interpole linéairement entre images
/// clés, reproduit donc le champ <em>sans aucune approximation</em>, et le fil
/// d'interface ne calcule rien pendant que la grille vit.
/// </para>
/// </summary>
public static class HypnoticField
{
    /// <summary>Côté de la grille.</summary>
    public const int GridSize = 3;

    /// <summary>Nombre de pixels.</summary>
    public const int CellCount = GridSize * GridSize;

    // ------------------------------------------------------------------
    // Motifs (ligne par ligne, « x » allumé, « . » éteint)
    // ------------------------------------------------------------------

    private static readonly double[] Cross = Mask("x.x/.x./x.x");
    private static readonly double[] Ring = Mask("xxx/x.x/xxx");
    private static readonly double[] Diamond = Mask(".x./x.x/.x.");
    private static readonly double[] Plus = Mask(".x./xxx/.x.");
    private static readonly double[] Corners = Mask("x.x/.../x.x");
    private static readonly double[] Center = Mask(".../.x./...");
    private static readonly double[] Full = Mask("xxx/xxx/xxx");
    private static readonly double[] Off = Mask(".../.../...");
    private static readonly double[] Column0 = Mask("x../x../x..");
    private static readonly double[] Column1 = Mask(".x./.x./.x.");
    private static readonly double[] Column2 = Mask("..x/..x/..x");

    /// <summary>Tour de l'anneau, dans le sens horaire depuis le coin supérieur gauche.</summary>
    private static readonly int[] RingOrder = [0, 1, 2, 5, 8, 7, 6, 3];

    // ------------------------------------------------------------------
    // Palettes (tirées de la vidéo de référence)
    // ------------------------------------------------------------------

    private static readonly HypnoticColor Blue = new(0x6F, 0xAE, 0xFF);
    private static readonly HypnoticColor Cyan = new(0x7F, 0xE6, 0xFF);
    private static readonly HypnoticColor Orange = new(0xFF, 0xB2, 0x6B);
    private static readonly HypnoticColor Coral = new(0xFF, 0x86, 0x7A);
    private static readonly HypnoticColor Peach = new(0xFF, 0xC4, 0x8E);
    private static readonly HypnoticColor Pink = new(0xFF, 0x8F, 0xA3);
    private static readonly HypnoticColor Lavender = new(0xB9, 0xA8, 0xFF);
    private static readonly HypnoticColor Mint = new(0x7F, 0xE8, 0xB0);
    private static readonly HypnoticColor Red = new(0xFF, 0x6B, 0x6B);

    /// <summary>Couleur de repos : la lumière chaude de la référence.</summary>
    public static HypnoticColor WarmLight => Peach;

    private static readonly Dictionary<HypnoticPreset, Choreography> Choreographies = Build();

    /// <summary>Vrai pour les préréglages qui bouclent tant que le travail dure.</summary>
    public static bool IsLooping(HypnoticPreset preset)
        => preset is not (HypnoticPreset.None or HypnoticPreset.Complete or HypnoticPreset.Error);

    /// <summary>
    /// Durée d'une boucle complète — motifs et dérive de couleur — ou de l'unique
    /// passage d'un préréglage ponctuel, en secondes.
    /// </summary>
    public static double PeriodSeconds(HypnoticPreset preset)
        => Choreographies.TryGetValue(preset, out Choreography? c) ? c.Period : 0;

    /// <summary>
    /// Durée moyenne d'un motif, en secondes : la cadence visible. La référence
    /// change de motif toutes les 150 à 250 ms.
    /// </summary>
    public static double StepSeconds(HypnoticPreset preset)
        => Choreographies.TryGetValue(preset, out Choreography? c) ? c.Sequence.Average(s => s.Duration) : 0;

    /// <summary>
    /// Préréglage effectif d'une activité, d'après son état de travail et le
    /// préréglage qu'elle déclare. L'état décide s'il y a mouvement ; le
    /// préréglage déclaré décide lequel.
    /// </summary>
    public static HypnoticPreset Resolve(ActivityMotionState state, HypnoticPreset declared) => state switch
    {
        ActivityMotionState.Completing => HypnoticPreset.Complete,
        ActivityMotionState.Error => HypnoticPreset.Error,
        ActivityMotionState.Attention => IsLooping(declared) ? declared : HypnoticPreset.Read,
        ActivityMotionState.Working => IsLooping(declared) ? declared : HypnoticPreset.Process,
        _ => HypnoticPreset.None
    };

    /// <summary>Image de repos : grille éteinte, sans halo, sans impulsion.</summary>
    public static HypnoticFrame Rest { get; } = new(Off, WarmLight, 0, 0, 0);

    /// <summary>
    /// Image fixe représentative, pour la réduction des animations : un motif
    /// allumé et sa couleur, sans le mouvement. Un préréglage ponctuel se fige
    /// sur son état final.
    /// </summary>
    public static HypnoticFrame StaticFrame(HypnoticPreset preset)
    {
        if (!Choreographies.ContainsKey(preset))
        {
            return Rest;
        }

        return Evaluate(preset, IsLooping(preset) ? 0 : PeriodSeconds(preset));
    }

    /// <summary>
    /// Évalue le champ à l'instant <paramref name="seconds"/>. Les boucles se
    /// répètent ; les préréglages ponctuels restent figés sur leur dernière image.
    /// </summary>
    public static HypnoticFrame Evaluate(HypnoticPreset preset, double seconds)
    {
        if (!Choreographies.TryGetValue(preset, out Choreography? c)
            || double.IsNaN(seconds)
            || double.IsInfinity(seconds))
        {
            return Rest;
        }

        double t = Math.Max(0, seconds);
        t = c.Loop ? t - (Math.Floor(t / c.Period) * c.Period) : Math.Min(t, c.Period);

        double[] cells = c.CellsAt(t);
        double mean = cells.Average();

        return new HypnoticFrame(
            cells,
            c.ColorAt(t),
            Math.Clamp(c.BloomBase + (c.BloomGain * mean), 0, 1),
            c.ShakeAt(t),
            Math.Clamp(0.2 + (0.8 * mean), 0, 1));
    }

    /// <summary>
    /// Les images clés exactes d'une boucle — ou de l'unique passage d'un
    /// préréglage ponctuel — avec leur avancement de 0 à 1.
    ///
    /// Entre deux images, toutes les grandeurs varient linéairement : un
    /// compositeur qui interpole linéairement reproduit le champ à l'identique.
    /// Pour une boucle, la dernière image est celle du départ.
    /// </summary>
    public static IReadOnlyList<(double Progress, HypnoticFrame Frame)> Keyframes(HypnoticPreset preset)
    {
        if (!Choreographies.TryGetValue(preset, out Choreography? c))
        {
            return [(0, Rest), (1, Rest)];
        }

        var times = new SortedSet<double>(c.Breakpoints()) { 0, c.Period };
        var frames = new List<(double, HypnoticFrame)>(times.Count);

        foreach (double time in times)
        {
            HypnoticFrame frame = c.Loop && time >= c.Period
                ? Evaluate(preset, 0)
                : Evaluate(preset, time);

            frames.Add((time / c.Period, frame));
        }

        return frames;
    }

    // ------------------------------------------------------------------
    // Chorégraphies
    // ------------------------------------------------------------------

    private static Dictionary<HypnoticPreset, Choreography> Build()
    {
        var map = new Dictionary<HypnoticPreset, Choreography>
        {
            // Lecture : un curseur parcourt la grille comme une ligne de texte,
            // suivi d'une traîne — ce qui dessine les marches bleues de la
            // référence au passage à la ligne.
            [HypnoticPreset.Read] = new(
                Repeat(ScanWithTrail(9, 0.09, 0.06), 2),
                [Blue, Cyan, Blue],
                Loop: true,
                BloomBase: 0.20,
                BloomGain: 0.70),

            // Réflexion : un serpent de trois pixels fait le tour de l'anneau ;
            // ses coudes forment les « L » orangés de la référence.
            [HypnoticPreset.Think] = new(
                Repeat(Snake(0.10, 0.06), 3),
                [Orange, Coral, Pink, Orange],
                Loop: true,
                BloomBase: 0.25,
                BloomGain: 0.75),

            // Recherche : une colonne balaie de gauche à droite, puis la grille
            // se tait un instant avant le balayage suivant.
            [HypnoticPreset.Search] = new(
                Repeat(
                [
                    new Step(Column0, 0.12, 0.06),
                    new Step(Column1, 0.12, 0.06),
                    new Step(Column2, 0.12, 0.06),
                    new Step(Scale(Full, 0.12), 0.12, 0.06)
                ], 3),
                [Cyan, Blue, Cyan],
                Loop: true,
                BloomBase: 0.20,
                BloomGain: 0.70),

            // Traitement — « Creating prototype » : croix, anneau, losange,
            // plein, coins, plus… toutes les 220 ms, et la dérive pêche → rose →
            // bleu → lavande sur quatre cycles.
            [HypnoticPreset.Process] = new(
                Repeat(
                [
                    new Step(Cross, 0.15, 0.07),
                    new Step(Ring, 0.15, 0.07),
                    new Step(Diamond, 0.15, 0.07),
                    new Step(Scale(Full, 0.55), 0.15, 0.07),
                    new Step(Cross, 0.15, 0.07),
                    new Step(Corners, 0.15, 0.07),
                    new Step(Plus, 0.15, 0.07),
                    new Step(Ring, 0.15, 0.07)
                ], 4),
                [Peach, Pink, Blue, Lavender, Peach],
                Loop: true,
                BloomBase: 0.30,
                BloomGain: 0.70),

            // Synchronisation : une colonne va d'un bord à l'autre et revient ;
            // la couleur passe d'un pôle froid à un pôle chaud.
            [HypnoticPreset.Sync] = new(
                Repeat(
                [
                    new Step(Column0, 0.13, 0.07),
                    new Step(Column1, 0.13, 0.07),
                    new Step(Column2, 0.13, 0.07),
                    new Step(Column1, 0.13, 0.07)
                ], 3),
                [Blue, Orange, Blue],
                Loop: true,
                BloomBase: 0.20,
                BloomGain: 0.70),

            // Dépôt : la lumière se resserre de l'anneau vers le centre, puis
            // s'éteint — la notch absorbe.
            [HypnoticPreset.Drop] = new(
                Repeat(
                [
                    new Step(Ring, 0.12, 0.06),
                    new Step(Diamond, 0.12, 0.06),
                    new Step(Center, 0.12, 0.06),
                    new Step(Scale(Center, 0.15), 0.12, 0.06)
                ], 2),
                [Peach, Orange, Peach],
                Loop: true,
                BloomBase: 0.30,
                BloomGain: 0.70),

            // Achèvement : tout s'allume d'un coup, se resserre, et un seul
            // pixel reste, apaisé.
            [HypnoticPreset.Complete] = new(
                [
                    new Step(Full, 0.12, 0.10),
                    new Step(Plus, 0.15, 0.10),
                    new Step(Center, 0.20, 0.28),
                    new Step(Scale(Center, 0.6), 0, 0)
                ],
                [Mint, Mint],
                Loop: false,
                BloomBase: 0.25,
                BloomGain: 0.75),

            // Échec : la croix clignote deux fois, la grille tremble, puis reste
            // faiblement allumée.
            [HypnoticPreset.Error] = new(
                [
                    new Step(Cross, 0.12, 0.04),
                    new Step(Off, 0.06, 0.04),
                    new Step(Cross, 0.12, 0.04),
                    new Step(Off, 0.06, 0.04),
                    new Step(Scale(Cross, 0.5), 0.28, 0)
                ],
                [Red, Red],
                Loop: false,
                BloomBase: 0.20,
                BloomGain: 0.70,
                Shake: [(0, 0), (0.08, 0.12), (0.16, -0.10), (0.26, 0.06), (0.36, -0.03), (0.44, 0)])
        };

        return map;
    }

    private static Step[] ScanWithTrail(int cells, double hold, double fade)
    {
        var steps = new Step[cells];

        for (int k = 0; k < cells; k++)
        {
            var mask = new double[CellCount];
            mask[k] = 1;

            if (k >= 1)
            {
                mask[k - 1] = 0.5;
            }

            if (k >= 2)
            {
                mask[k - 2] = 0.2;
            }

            steps[k] = new Step(mask, hold, fade);
        }

        return steps;
    }

    private static Step[] Snake(double hold, double fade)
    {
        var steps = new Step[RingOrder.Length];

        for (int k = 0; k < RingOrder.Length; k++)
        {
            var mask = new double[CellCount];
            mask[RingOrder[k]] = 1;
            mask[RingOrder[(k + RingOrder.Length - 1) % RingOrder.Length]] = 0.7;
            mask[RingOrder[(k + RingOrder.Length - 2) % RingOrder.Length]] = 0.35;
            steps[k] = new Step(mask, hold, fade);
        }

        return steps;
    }

    private static Step[] Repeat(Step[] steps, int times)
    {
        var result = new Step[steps.Length * times];

        for (int i = 0; i < times; i++)
        {
            Array.Copy(steps, 0, result, i * steps.Length, steps.Length);
        }

        return result;
    }

    private static double[] Scale(double[] mask, double factor) => mask.Select(v => v * factor).ToArray();

    private static double[] Mask(string pattern)
    {
        string compact = pattern.Replace("/", string.Empty, StringComparison.Ordinal);
        var mask = new double[CellCount];

        for (int i = 0; i < CellCount; i++)
        {
            mask[i] = compact[i] == 'x' ? 1 : 0;
        }

        return mask;
    }

    /// <summary>Un motif tenu, puis fondu vers le suivant.</summary>
    private sealed record Step(double[] Cells, double Hold, double Fade)
    {
        public double Duration => Hold + Fade;
    }

    /// <summary>
    /// Une chorégraphie : une suite de motifs, une dérive de couleur répartie
    /// uniformément sur la période, et éventuellement une secousse.
    /// </summary>
    private sealed record Choreography(
        Step[] Sequence,
        HypnoticColor[] Palette,
        bool Loop,
        double BloomBase,
        double BloomGain,
        (double Time, double Offset)[]? Shake = null)
    {
        public double Period { get; } = Sequence.Sum(s => s.Duration);

        public double[] CellsAt(double t)
        {
            double start = 0;

            for (int i = 0; i < Sequence.Length; i++)
            {
                Step step = Sequence[i];
                double end = start + step.Duration;

                if (t < end || i == Sequence.Length - 1)
                {
                    if (t <= start + step.Hold || step.Fade <= 0)
                    {
                        return [.. step.Cells];
                    }

                    double[] next = i + 1 < Sequence.Length
                        ? Sequence[i + 1].Cells
                        : Loop ? Sequence[0].Cells : step.Cells;

                    double k = Math.Clamp((t - start - step.Hold) / step.Fade, 0, 1);
                    var cells = new double[CellCount];

                    for (int c = 0; c < CellCount; c++)
                    {
                        cells[c] = step.Cells[c] + ((next[c] - step.Cells[c]) * k);
                    }

                    return cells;
                }

                start = end;
            }

            return [.. Sequence[^1].Cells];
        }

        public HypnoticColor ColorAt(double t)
        {
            if (Palette.Length == 1)
            {
                return Palette[0];
            }

            double position = t / Period * (Palette.Length - 1);
            int index = Math.Clamp((int)Math.Floor(position), 0, Palette.Length - 2);

            return HypnoticColor.Lerp(Palette[index], Palette[index + 1], position - index);
        }

        public double ShakeAt(double t)
        {
            if (Shake is null || Shake.Length == 0)
            {
                return 0;
            }

            for (int i = 0; i < Shake.Length - 1; i++)
            {
                (double t0, double v0) = Shake[i];
                (double t1, double v1) = Shake[i + 1];

                if (t <= t1)
                {
                    return v0 + ((v1 - v0) * Math.Clamp((t - t0) / (t1 - t0), 0, 1));
                }
            }

            return Shake[^1].Offset;
        }

        public IEnumerable<double> Breakpoints()
        {
            double start = 0;

            foreach (Step step in Sequence)
            {
                yield return start;
                yield return start + step.Hold;
                start += step.Duration;
            }

            for (int i = 0; i < Palette.Length; i++)
            {
                yield return Period * i / (Palette.Length - 1 == 0 ? 1 : Palette.Length - 1);
            }

            if (Shake is not null)
            {
                foreach ((double time, _) in Shake)
                {
                    yield return Math.Min(time, Period);
                }
            }
        }
    }
}
