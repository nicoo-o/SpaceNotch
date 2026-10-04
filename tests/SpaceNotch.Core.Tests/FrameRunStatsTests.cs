using System;
using System.Collections.Generic;
using SpaceNotch.Core.Animation;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Mesure de fluidité (jalon 1 de la session « fluidité, UX, feuille de route ») :
/// une rafale d'images, de l'abonnement du premier animateur au départ du
/// dernier, résumée en une ligne de journal.
/// </summary>
public sealed class FrameRunStatsTests
{
    [Fact]
    public void Une_rafale_vide_ne_produit_aucun_releve()
    {
        var run = new FrameRunStats();

        Assert.Null(run.Finish());
    }

    [Fact]
    public void Les_centiles_et_le_maximum_portent_sur_les_intervalles_et_les_couts()
    {
        var run = new FrameRunStats();

        // La première image n'a pas d'intervalle : elle ouvre la rafale.
        run.Add(double.NaN, 1);

        for (int i = 1; i <= 19; i++)
        {
            run.Add(8, 2);
        }

        run.Add(40, 12);

        FrameRunReport report = run.Finish()!.Value;

        Assert.Equal(21, report.Frames);
        Assert.Equal(8, report.IntervalP50, 3);
        Assert.Equal(8, report.IntervalP95, 3);
        Assert.Equal(40, report.IntervalMax, 3);
        Assert.Equal(2, report.CostP50, 3);
        Assert.Equal(12, report.CostMax, 3);
        Assert.Equal(19 * 8 + 40, report.DurationMs, 3);
    }

    [Fact]
    public void Une_image_manquee_est_un_intervalle_plus_long_qu_une_fois_et_demie_la_cadence()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 1);

        // Cadence de 120 Hz, deux trous : 16,7 ms (une image sautée) et 25 ms.
        foreach (double interval in new[] { 8.3, 8.3, 8.4, 16.7, 8.3, 25, 8.3, 8.3, 8.4, 8.3 })
        {
            run.Add(interval, 1);
        }

        FrameRunReport report = run.Finish()!.Value;

        Assert.Equal(8.3, report.CadenceMs, 1);
        Assert.Equal(2, report.Missed);
    }

    [Fact]
    public void Un_intervalle_de_plus_de_100_ms_est_une_pause_pas_une_image_manquee()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 1);

        // L'horloge reste abonnée, mais rien n'est dessiné pendant 4,9 s : ce
        // n'est pas une saccade que l'œil voit, c'est une animation à l'arrêt.
        foreach (double interval in new[] { 4.2, 4.2, 4.2, 4926, 4.2, 9, 4.2, 4.2, 4.2, 4.2 })
        {
            run.Add(interval, 1);
        }

        FrameRunReport report = run.Finish()!.Value;

        Assert.Equal(1, report.Missed);
        Assert.Equal(1, report.Pauses);
    }

    [Fact]
    public void Un_intervalle_nul_ne_fausse_pas_la_cadence()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 1);

        // Le signal d'image peut être levé deux fois pour la même image : un
        // intervalle nul n'est pas une image, il ne doit pas tirer la cadence à 0.
        foreach (double interval in new[] { 0, 0, 4.2, 4.2, 4.2, 4.2, 9, 4.2, 4.2, 4.2 })
        {
            run.Add(interval, 1);
        }

        FrameRunReport report = run.Finish()!.Value;

        Assert.Equal(4.2, report.CadenceMs, 1);
        Assert.Equal(1, report.Missed);
    }

    [Fact]
    public void Le_cout_est_compare_aux_budgets_de_120_et_60_Hz()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 3);
        run.Add(8, 9);
        run.Add(8, 17);
        run.Add(8, 4);

        FrameRunReport report = run.Finish()!.Value;

        Assert.Equal(2, report.OverBudget120);
        Assert.Equal(1, report.OverBudget60);
    }

    [Fact]
    public void Terminer_remet_la_rafale_a_zero()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 1);
        run.Add(8, 1);
        run.Finish();

        Assert.Null(run.Finish());
    }

    [Fact]
    public void La_ligne_de_journal_est_en_francais_et_a_virgule_fixe()
    {
        var run = new FrameRunStats();
        run.Add(double.NaN, 1.5);
        run.Add(8.5, 2.5);

        string line = run.Finish()!.Value.ToLogLine("Closed→Expanded");

        // Centile au rang le plus proche : sur deux coûts, la médiane est le premier.
        Assert.StartsWith("[IMAGES] Closed→Expanded : 2 images en 8,5 ms", line, System.StringComparison.Ordinal);
        Assert.Contains("intervalle p50 8,5 / p95 8,5 / max 8,5 ms", line, System.StringComparison.Ordinal);
        Assert.Contains("coût p50 1,5 / p95 2,5 / max 2,5 ms", line, System.StringComparison.Ordinal);
    }
}
