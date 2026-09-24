using System;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Features.Demo;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Le test de torture du plan : Spotify, le volume, quatre messages Discord, un
/// téléchargement, un casque et une activité qui réfléchit — rejoués sur le
/// vrai gestionnaire d'activités. L'objectif : aucune impression de chaos.
/// </summary>
public class TortureTests
{
    [Fact]
    public void EverythingAtOnce_StaysCalm()
    {
        DateTimeOffset start = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset now = start;
        var manager = new ActivityManager(() => now);

        int maxStack = 0;
        bool musicSurvived = true;

        foreach (DemoStep step in DemoScenario.Steps())
        {
            now = start + step.At;
            manager.ExpireOverdue(now);

            if (step.Post?.Invoke(now) is { } post)
            {
                IslandActivity? before = manager.CurrentActivity;
                ActivityInterruption decision = ActivityPolicies.Decide(before, post, NotchPresentation.Compact);

                // Rien, dans une journée ordinaire, ne mérite d'ouvrir la notch
                // de force : seuls un appel ou une urgence le peuvent.
                Assert.NotEqual(ActivityInterruption.Interrupt, decision);

                manager.PostActivity(post);
            }

            if (step.RemoveId is { } id)
            {
                manager.RemoveActivity(id);
            }

            maxStack = Math.Max(maxStack, manager.Count);

            if (step.At < TimeSpan.FromSeconds(50))
            {
                musicSurvived &= manager.GetActiveActivities().Any(a => a.Id == DemoScenario.MediaId);
            }

            // Tant que la musique joue, la notch présente toujours quelque chose
            // — une seule chose à la fois, jamais un vide entre deux arrivées.
            if (step.At < TimeSpan.FromSeconds(50))
            {
                Assert.NotNull(manager.CurrentActivity);
            }
        }

        // La musique n'a jamais été détruite par ce qui l'a recouverte.
        Assert.True(musicSurvived);

        // Quatre messages ne font qu'un groupe : la pile reste lisible.
        Assert.InRange(maxStack, 2, 5);

        // À la fin, la notch retourne au repos.
        now = start + TimeSpan.FromSeconds(60);
        manager.ExpireOverdue(now);
        Assert.Equal(0, manager.Count);
    }

    [Fact]
    public void TheScenario_ReplaysTheVideoReference()
    {
        var thinking = DemoScenario.Steps()
            .Select(s => s.Post?.Invoke(DateTimeOffset.UtcNow))
            .Where(a => a?.Id == DemoScenario.ThinkingId)
            .Select(a => HypnoticField.Resolve(a!.MotionState, a.MotionPreset))
            .ToArray();

        Assert.Equal(
            [HypnoticPreset.Think, HypnoticPreset.Read, HypnoticPreset.Process, HypnoticPreset.Complete],
            thinking);
    }

    [Fact]
    public void TheScenario_IsChronological()
    {
        var steps = DemoScenario.Steps();

        for (int i = 1; i < steps.Count; i++)
        {
            Assert.True(steps[i].At >= steps[i - 1].At);
        }
    }
}
