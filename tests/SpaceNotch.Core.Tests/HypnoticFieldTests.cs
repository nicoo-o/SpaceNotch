using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// La grille hypnotique, vérifiée comme une fonction : bornée, périodique là où
/// elle boucle, cuisable sans perte, et fidèle à la référence vidéo — cadence,
/// motifs, palettes.
/// </summary>
public class HypnoticFieldTests
{
    private static readonly HypnoticPreset[] Loops =
    [
        HypnoticPreset.Read,
        HypnoticPreset.Think,
        HypnoticPreset.Search,
        HypnoticPreset.Process,
        HypnoticPreset.Sync,
        HypnoticPreset.Drop
    ];

    private static readonly HypnoticPreset[] Animated =
    [
        .. Loops,
        HypnoticPreset.Complete,
        HypnoticPreset.Error
    ];

    [Fact]
    public void EveryFrame_IsANineCellGrid_WithinBounds()
    {
        foreach (HypnoticPreset preset in Enum.GetValues<HypnoticPreset>())
        {
            double period = Math.Max(0.5, HypnoticField.PeriodSeconds(preset));

            for (double t = 0; t <= period * 2; t += period / 97)
            {
                HypnoticFrame frame = HypnoticField.Evaluate(preset, t);

                Assert.Equal(HypnoticField.CellCount, frame.Cells.Count);
                Assert.All(frame.Cells, cell => Assert.InRange(cell, 0, 1));
                Assert.InRange(frame.Bloom, 0, 1);
                Assert.InRange(frame.AmbientPulse, 0, 1);
                Assert.InRange(frame.ShakeX, -0.2, 0.2);
            }
        }
    }

    [Fact]
    public void TheCadence_MatchesTheReference()
    {
        // La référence change de motif toutes les 150 à 250 ms : plus vite, la
        // grille scintille ; plus lentement, elle ne se lit plus comme un travail.
        foreach (HypnoticPreset preset in Loops)
        {
            Assert.InRange(HypnoticField.StepSeconds(preset), 0.14, 0.26);
        }

        Assert.Equal(0.22, HypnoticField.StepSeconds(HypnoticPreset.Process), 3);
    }

    [Fact]
    public void Loops_RestartSeamlessly()
    {
        foreach (HypnoticPreset preset in Loops)
        {
            double period = HypnoticField.PeriodSeconds(preset);
            HypnoticFrame start = HypnoticField.Evaluate(preset, 0);
            HypnoticFrame end = HypnoticField.Evaluate(preset, period - 1e-6);

            for (int i = 0; i < HypnoticField.CellCount; i++)
            {
                Assert.Equal(start.Cells[i], end.Cells[i], 3);
            }

            Assert.InRange(Math.Abs(start.Color.R - end.Color.R), 0, 1);
            Assert.InRange(Math.Abs(start.Color.G - end.Color.G), 0, 1);
            Assert.InRange(Math.Abs(start.Color.B - end.Color.B), 0, 1);
        }
    }

    [Fact]
    public void TheKeyframes_ReproduceTheFieldExactly()
    {
        // Le compositeur interpole linéairement entre images clés : si les images
        // clés sont bien les points de rupture, la grille rejouée par le GPU est
        // celle que décrit le cœur, à l'arrondi de couleur près.
        var random = new Random(7);

        foreach (HypnoticPreset preset in Animated)
        {
            IReadOnlyList<(double Progress, HypnoticFrame Frame)> keys = HypnoticField.Keyframes(preset);
            double period = HypnoticField.PeriodSeconds(preset);

            Assert.Equal(0, keys[0].Progress);
            Assert.Equal(1, keys[^1].Progress, 9);

            for (int n = 0; n < 200; n++)
            {
                double progress = random.NextDouble();
                int i = 0;

                while (i < keys.Count - 2 && keys[i + 1].Progress < progress)
                {
                    i++;
                }

                (double p0, HypnoticFrame f0) = keys[i];
                (double p1, HypnoticFrame f1) = keys[i + 1];
                double k = p1 > p0 ? (progress - p0) / (p1 - p0) : 0;

                HypnoticFrame expected = HypnoticField.Evaluate(preset, progress * period);

                for (int c = 0; c < HypnoticField.CellCount; c++)
                {
                    double interpolated = f0.Cells[c] + ((f1.Cells[c] - f0.Cells[c]) * k);
                    Assert.Equal(expected.Cells[c], interpolated, 6);
                }

                Assert.Equal(expected.Bloom, f0.Bloom + ((f1.Bloom - f0.Bloom) * k), 6);
                Assert.Equal(expected.ShakeX, f0.ShakeX + ((f1.ShakeX - f0.ShakeX) * k), 6);
                Assert.InRange(Math.Abs(expected.Color.R - (f0.Color.R + ((f1.Color.R - f0.Color.R) * k))), 0, 1.5);
            }
        }
    }

    [Fact]
    public void Palettes_SayWhatIsHappening()
    {
        // La couleur porte l'état, comme dans la référence : la lecture est
        // froide, la réflexion chaude, le succès vert, l'échec rouge.
        HypnoticColor read = HypnoticField.Evaluate(HypnoticPreset.Read, 0).Color;
        HypnoticColor think = HypnoticField.Evaluate(HypnoticPreset.Think, 0).Color;
        HypnoticColor done = HypnoticField.StaticFrame(HypnoticPreset.Complete).Color;
        HypnoticColor error = HypnoticField.StaticFrame(HypnoticPreset.Error).Color;

        Assert.True(read.B > read.R);
        Assert.True(think.R > think.B);
        Assert.True(done.G > done.R && done.G > done.B);
        Assert.True(error.R > error.G && error.R > error.B);
    }

    [Fact]
    public void Process_DriftsThroughPeachPinkBlueAndLavender()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Process);

        HypnoticColor third = HypnoticField.Evaluate(HypnoticPreset.Process, period / 2).Color;

        // Au milieu de la boucle, la dérive est passée du chaud au froid.
        Assert.True(third.B > third.R, $"Couleur attendue froide à mi-boucle : {third}");
    }

    [Fact]
    public void Think_IsASnakeOfThreePixels()
    {
        // Pendant la tenue d'un motif, trois pixels au plus sont allumés — leurs
        // coudes dessinent les « L » de la référence.
        HypnoticFrame frame = HypnoticField.Evaluate(HypnoticPreset.Think, 0.05);

        Assert.Equal(3, frame.Cells.Count(cell => cell > 0.1));
        Assert.Equal(1, frame.Cells.Max());
        Assert.Equal(0, frame.Cells[4]);
    }

    [Fact]
    public void Drop_ConvergesTowardTheCenter()
    {
        double previous = double.MaxValue;

        // Tenues successives : anneau, losange, centre.
        foreach (double t in new[] { 0.05, 0.23, 0.41 })
        {
            double distance = MeanDistanceFromCenter(HypnoticField.Evaluate(HypnoticPreset.Drop, t));

            Assert.True(distance < previous, $"La lumière s'éloigne à t={t}");
            previous = distance;
        }
    }

    [Fact]
    public void Sync_FlowsBothWays_AndSearch_SweepsRight()
    {
        double[] sync = Enumerable.Range(0, 4)
            .Select(i => CentroidX(HypnoticField.Evaluate(HypnoticPreset.Sync, (i * 0.2) + 0.05)))
            .ToArray();

        Assert.True(sync[0] < 0 && sync[2] > 0 && Math.Abs(sync[1]) < 1e-9 && Math.Abs(sync[3]) < 1e-9);

        double first = CentroidX(HypnoticField.Evaluate(HypnoticPreset.Search, 0.05));
        double last = CentroidX(HypnoticField.Evaluate(HypnoticPreset.Search, 0.41));

        Assert.True(last > first);
    }

    [Fact]
    public void Complete_FlashesThenSettlesOnOnePixel()
    {
        HypnoticFrame flash = HypnoticField.Evaluate(HypnoticPreset.Complete, 0.05);
        HypnoticFrame end = HypnoticField.StaticFrame(HypnoticPreset.Complete);

        Assert.All(flash.Cells, cell => Assert.Equal(1, cell));
        Assert.True(flash.Bloom > end.Bloom);
        Assert.Equal(1, end.Cells.Count(cell => cell > 0.1));
        Assert.Equal(0.6, end.Cells[4], 6);
        Assert.False(HypnoticField.IsLooping(HypnoticPreset.Complete));
    }

    [Fact]
    public void Error_BlinksTwice_Shakes_ThenStaysDim()
    {
        double[] centre = Enumerable.Range(0, 60)
            .Select(i => HypnoticField.Evaluate(HypnoticPreset.Error, i * 0.8 / 60).Cells[4])
            .ToArray();

        // Un clignotement : la croix allumée, puis éteinte. Les fondus durent
        // quelques centièmes de seconde, d'où un suivi par état plutôt qu'une
        // comparaison entre deux échantillons voisins.
        int blinks = 0;
        bool lit = false;

        foreach (double value in centre)
        {
            if (value > 0.9)
            {
                lit = true;
            }
            else if (lit && value < 0.1)
            {
                blinks++;
                lit = false;
            }
        }

        Assert.Equal(2, blinks);
        Assert.Contains(Enumerable.Range(0, 40), i => Math.Abs(HypnoticField.Evaluate(HypnoticPreset.Error, i * 0.01).ShakeX) > 0.05);

        HypnoticFrame end = HypnoticField.StaticFrame(HypnoticPreset.Error);
        Assert.Equal(0, end.ShakeX, 9);
        Assert.Equal(0.5, end.Cells[4], 6);
    }

    [Fact]
    public void None_DoesNotMove_AndIsDark()
    {
        Assert.Equal(HypnoticField.Rest, HypnoticField.Evaluate(HypnoticPreset.None, 12.5));
        Assert.Equal(HypnoticField.Rest, HypnoticField.StaticFrame(HypnoticPreset.None));
        Assert.All(HypnoticField.Rest.Cells, cell => Assert.Equal(0, cell));
        Assert.Equal(0, HypnoticField.Rest.AmbientPulse);
    }

    [Fact]
    public void ReducedMotion_KeepsAPatternLit()
    {
        foreach (HypnoticPreset preset in Animated)
        {
            HypnoticFrame still = HypnoticField.StaticFrame(preset);

            Assert.Contains(still.Cells, cell => cell > 0.4);
        }
    }

    [Theory]
    [InlineData(ActivityMotionState.Idle, HypnoticPreset.Search, HypnoticPreset.None)]
    [InlineData(ActivityMotionState.Complete, HypnoticPreset.Search, HypnoticPreset.None)]
    [InlineData(ActivityMotionState.Working, HypnoticPreset.None, HypnoticPreset.Process)]
    [InlineData(ActivityMotionState.Working, HypnoticPreset.Sync, HypnoticPreset.Sync)]
    [InlineData(ActivityMotionState.Attention, HypnoticPreset.None, HypnoticPreset.Read)]
    [InlineData(ActivityMotionState.Working, HypnoticPreset.Complete, HypnoticPreset.Process)]
    [InlineData(ActivityMotionState.Completing, HypnoticPreset.Drop, HypnoticPreset.Complete)]
    [InlineData(ActivityMotionState.Error, HypnoticPreset.Sync, HypnoticPreset.Error)]
    public void TheStateDecidesWhether_ThePresetDecidesHow(
        ActivityMotionState state,
        HypnoticPreset declared,
        HypnoticPreset expected)
    {
        Assert.Equal(expected, HypnoticField.Resolve(state, declared));
    }

    private static double CentroidX(HypnoticFrame frame)
    {
        double total = frame.Cells.Sum();
        double weighted = 0;

        for (int i = 0; i < HypnoticField.CellCount; i++)
        {
            weighted += frame.Cells[i] * ((i % 3) - 1);
        }

        return total > 0 ? weighted / total : 0;
    }

    private static double MeanDistanceFromCenter(HypnoticFrame frame)
    {
        double total = frame.Cells.Sum();
        double weighted = 0;

        for (int i = 0; i < HypnoticField.CellCount; i++)
        {
            double dx = (i % 3) - 1;
            double dy = (i / 3) - 1;
            weighted += frame.Cells[i] * Math.Sqrt((dx * dx) + (dy * dy));
        }

        return weighted / total;
    }
}
