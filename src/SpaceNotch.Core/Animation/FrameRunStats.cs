using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpaceNotch.Core.Animation;

/// <summary>
/// Relevé d'une rafale d'images : de l'abonnement du premier animateur à
/// l'horloge d'images jusqu'au départ du dernier.
///
/// <para>
/// Sert la mesure de fluidité, derrière un drapeau de diagnostic éteint par
/// défaut : une ligne de journal par rafale, jamais un relevé par image, pour
/// que la mesure ne crée pas la charge qu'elle mesure. Rien ne quitte la
/// machine (l'application promet zéro télémétrie).
/// </para>
/// </summary>
public sealed class FrameRunStats
{
    /// <summary>Budget d'une image à 120 Hz, en millisecondes.</summary>
    public const double Budget120 = 1000.0 / 120;

    /// <summary>Budget d'une image à 60 Hz, en millisecondes.</summary>
    public const double Budget60 = 1000.0 / 60;

    private readonly List<double> _intervals = [];
    private readonly List<double> _costs = [];

    /// <summary>
    /// Une image.
    /// </summary>
    /// <param name="intervalMs">Temps depuis l'image précédente ; <see cref="double.NaN"/> pour la première.</param>
    /// <param name="costMs">Temps passé par les animateurs sur le fil d'interface pour cette image.</param>
    public void Add(double intervalMs, double costMs)
    {
        if (double.IsFinite(intervalMs) && intervalMs >= 0)
        {
            _intervals.Add(intervalMs);
        }

        if (double.IsFinite(costMs) && costMs >= 0)
        {
            _costs.Add(costMs);
        }
    }

    /// <summary>Clôt la rafale et la résume ; <c>null</c> si elle n'a vu aucune image.</summary>
    public FrameRunReport? Finish()
    {
        if (_costs.Count == 0)
        {
            _intervals.Clear();
            return null;
        }

        double[] intervals = [.. _intervals];
        double[] costs = [.. _costs];
        Array.Sort(intervals);
        Array.Sort(costs);
        _intervals.Clear();
        _costs.Clear();

        // La cadence de l'écran se lit sur les intervalles les plus courts : le
        // dixième centile ignore les images manquées qui gonflent la médiane.
        double cadence = Percentile(intervals, 0.10);
        int missed = 0;
        double duration = 0;

        foreach (double interval in intervals)
        {
            duration += interval;

            if (cadence > 0 && interval > cadence * 1.5)
            {
                missed++;
            }
        }

        int over120 = 0, over60 = 0;

        foreach (double cost in costs)
        {
            over120 += cost > Budget120 ? 1 : 0;
            over60 += cost > Budget60 ? 1 : 0;
        }

        return new FrameRunReport(
            costs.Length,
            duration,
            cadence,
            Percentile(intervals, 0.50),
            Percentile(intervals, 0.95),
            intervals.Length == 0 ? 0 : intervals[^1],
            missed,
            Percentile(costs, 0.50),
            Percentile(costs, 0.95),
            costs[^1],
            over120,
            over60);
    }

    /// <summary>Centile au rang le plus proche, sur des valeurs triées.</summary>
    private static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 0)
        {
            return 0;
        }

        int rank = (int)Math.Ceiling(p * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>Résumé d'une rafale d'images. Durées en millisecondes.</summary>
public readonly record struct FrameRunReport(
    int Frames,
    double DurationMs,
    double CadenceMs,
    double IntervalP50,
    double IntervalP95,
    double IntervalMax,
    int Missed,
    double CostP50,
    double CostP95,
    double CostMax,
    int OverBudget120,
    int OverBudget60)
{
    private static readonly CultureInfo French = CultureInfo.GetCultureInfo("fr-FR");

    /// <summary>Une ligne de journal, préfixée <c>[IMAGES]</c>, avec le contexte de la rafale.</summary>
    public string ToLogLine(string context) => string.Format(
        French,
        "[IMAGES] {0} : {1} images en {2:0.0} ms · cadence {3:0.0} ms · intervalle p50 {4:0.0} / p95 {5:0.0} / max {6:0.0} ms · manquées : {7} · coût p50 {8:0.0} / p95 {9:0.0} / max {10:0.0} ms · > 8,3 ms : {11} · > 16,7 ms : {12}",
        context,
        Frames,
        DurationMs,
        CadenceMs,
        IntervalP50,
        IntervalP95,
        IntervalMax,
        Missed,
        CostP50,
        CostP95,
        CostMax,
        OverBudget120,
        OverBudget60);
}
