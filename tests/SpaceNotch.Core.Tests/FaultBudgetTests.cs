using System;
using SpaceNotch.Core.State;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Exception non gérée (n° 33) : l'application continue, et le journal la garde
/// sans être noyé quand un minuteur échoue à chaque battement.
/// </summary>
public sealed class FaultBudgetTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 5, 20, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Les_premieres_exceptions_d_une_minute_sont_journalisees_en_entier()
    {
        var budget = new FaultBudget();

        for (int i = 0; i < FaultBudget.LoggedPerWindow; i++)
        {
            FaultVerdict verdict = budget.Record(Start.AddSeconds(i));
            Assert.True(verdict.Log);
            Assert.Equal(0, verdict.Silenced);
        }
    }

    [Fact]
    public void Au_dela_elles_sont_comptees_sans_etre_ecrites()
    {
        var budget = new FaultBudget();

        for (int i = 0; i < FaultBudget.LoggedPerWindow; i++)
        {
            budget.Record(Start.AddSeconds(i));
        }

        Assert.False(budget.Record(Start.AddSeconds(10)).Log);
        Assert.False(budget.Record(Start.AddSeconds(11)).Log);
    }

    // Les fonctionnalités battent sur le pool de threads (System.Threading.Timer) :
    // une exception y passe par AppDomain.UnhandledException, qui ferme toujours
    // le processus. Leur rappel gardé la signale à l'hôte et la fonctionnalité continue.

    private sealed class TickingFeature : SpaceNotch.Core.Features.IslandFeatureBase
    {
        public TickingFeature()
            : base("feature.tick", "Battement", new SpaceNotch.Core.Activities.ActivityManager(), new SpaceNotch.Core.Events.EventBus())
        {
        }

        public int Ticks { get; private set; }

        public System.Threading.TimerCallback Callback(bool fail) => Guarded(() =>
        {
            Ticks++;

            if (fail)
            {
                throw new InvalidOperationException("battement en échec");
            }
        });

        protected override System.Threading.Tasks.Task OnStartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;

        protected override System.Threading.Tasks.Task OnStopAsync() => System.Threading.Tasks.Task.CompletedTask;
    }

    [Fact]
    public void Un_battement_de_fonctionnalite_en_echec_est_signale_sans_lever()
    {
        var feature = new TickingFeature();
        Exception? reported = null;
        feature.ErrorReported += (_, ex) => reported = ex;

        feature.Callback(fail: true)(null);
        feature.Callback(fail: false)(null);

        Assert.IsType<InvalidOperationException>(reported);
        Assert.Equal(2, feature.Ticks);
    }

    [Fact]
    public void La_minute_suivante_rejournalise_et_dit_combien_ont_ete_tues()
    {
        var budget = new FaultBudget();

        for (int i = 0; i < FaultBudget.LoggedPerWindow + 3; i++)
        {
            budget.Record(Start.AddSeconds(i));
        }

        FaultVerdict next = budget.Record(Start + FaultBudget.Window + TimeSpan.FromSeconds(1));

        Assert.True(next.Log);
        Assert.Equal(3, next.Silenced);
        Assert.Equal(0, budget.Record(Start + FaultBudget.Window + TimeSpan.FromSeconds(2)).Silenced);
    }
}
