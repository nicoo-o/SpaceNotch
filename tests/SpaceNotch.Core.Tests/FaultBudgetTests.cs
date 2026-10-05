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
