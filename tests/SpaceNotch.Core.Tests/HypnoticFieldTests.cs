using System;
using System.Linq;
using SpaceNotch.Core.Motion;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Le mouvement hypnotique, vérifié comme une fonction : borné, périodique là où
/// il boucle, et fidèle à ce que chaque préréglage signifie.
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

    [Fact]
    public void EveryPreset_StaysInsideTheNotch()
    {
        // La matière vit dans la notch et ne s'en échappe jamais : aucune
        // particule ne quitte le cadre, aucune intensité ne sort de [0, 1].
        foreach (HypnoticPreset preset in Enum.GetValues<HypnoticPreset>())
        {
            double period = Math.Max(0.5, HypnoticField.PeriodSeconds(preset));

            for (double t = 0; t <= period * 2; t += period / 97)
            {
                HypnoticFrame frame = HypnoticField.Evaluate(preset, t);

                Assert.Equal(HypnoticField.MoteCount, frame.Motes.Count);
                Assert.InRange(frame.CoreIntensity, 0, 1);
                Assert.InRange(frame.HaloIntensity, 0, 1);
                Assert.InRange(frame.AmbientPulse, 0, 1);
                Assert.InRange(frame.CoreScale, 0.5, 1.6);
                Assert.InRange(frame.CoreOffsetX, -0.2, 0.2);

                Assert.All(frame.Motes, mote =>
                {
                    Assert.InRange(mote.X, -1, 1);
                    Assert.InRange(mote.Y, -1, 1);
                    Assert.InRange(mote.Intensity, 0, 1);
                    Assert.InRange(mote.Scale, 0, 1.5);
                });
            }
        }
    }

    [Fact]
    public void Loops_RestartSeamlessly()
    {
        // Le compositeur rejoue la boucle seul : la fin doit retomber sur le
        // départ, sinon chaque reprise se verrait comme un sursaut. Les particules
        // éteintes peuvent sauter — elles sont invisibles à cet instant.
        foreach (HypnoticPreset preset in Loops)
        {
            double period = HypnoticField.PeriodSeconds(preset);
            HypnoticFrame start = HypnoticField.Evaluate(preset, 0);
            HypnoticFrame end = HypnoticField.Evaluate(preset, period - 1e-6);

            Assert.Equal(start.CoreScale, end.CoreScale, 3);
            Assert.Equal(start.CoreIntensity, end.CoreIntensity, 3);
            Assert.Equal(start.AmbientPulse, end.AmbientPulse, 3);

            for (int i = 0; i < HypnoticField.MoteCount; i++)
            {
                Assert.Equal(start.Motes[i].Intensity, end.Motes[i].Intensity, 3);

                if (start.Motes[i].Intensity > 0.05)
                {
                    Assert.Equal(start.Motes[i].X, end.Motes[i].X, 3);
                    Assert.Equal(start.Motes[i].Y, end.Motes[i].Y, 3);
                }
            }
        }
    }

    [Fact]
    public void TheBakedLoop_EndsWhereItStarts()
    {
        foreach (HypnoticPreset preset in Loops)
        {
            var samples = HypnoticField.Sample(preset);

            Assert.Equal(HypnoticField.DefaultSamples + 1, samples.Count);
            Assert.Equal(0, samples[0].Progress);
            Assert.Equal(1, samples[^1].Progress);
            Assert.Equal(samples[0].Frame.CoreScale, samples[^1].Frame.CoreScale, 6);
        }
    }

    [Fact]
    public void TheRhythm_CarriesTheMeaning()
    {
        // La lecture respire, le traitement bat : un rythme plus lent pour ce qui
        // lit, plus rapide pour ce qui travaille dur.
        Assert.True(HypnoticField.PeriodSeconds(HypnoticPreset.Read) > HypnoticField.PeriodSeconds(HypnoticPreset.Process));
        Assert.True(HypnoticField.PeriodSeconds(HypnoticPreset.Think) > HypnoticField.PeriodSeconds(HypnoticPreset.Search));
    }

    [Fact]
    public void Complete_ConvergesPulsesAndGoesQuiet()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Complete);

        HypnoticFrame peak = HypnoticField.Evaluate(HypnoticPreset.Complete, period * 0.5);
        HypnoticFrame end = HypnoticField.Evaluate(HypnoticPreset.Complete, period);
        HypnoticFrame after = HypnoticField.Evaluate(HypnoticPreset.Complete, period * 5);

        Assert.True(peak.CoreScale > 1.3, "L'achèvement doit produire une impulsion visible.");
        Assert.Equal(1.0, end.CoreScale, 2);
        Assert.All(end.Motes, mote => Assert.Equal(0, mote.Intensity, 3));

        // Un préréglage ponctuel reste figé sur sa dernière image : il ne boucle pas.
        Assert.Equal(end.CoreScale, after.CoreScale, 6);
        Assert.Equal(end.AmbientPulse, after.AmbientPulse, 6);
        Assert.Equal(end.Motes, after.Motes);
        Assert.False(HypnoticField.IsLooping(HypnoticPreset.Complete));
    }

    [Fact]
    public void Error_Disperses_AndShakesOnlyBriefly()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Error);

        HypnoticFrame start = HypnoticField.Evaluate(HypnoticPreset.Error, 0);
        HypnoticFrame end = HypnoticField.Evaluate(HypnoticPreset.Error, period);

        Assert.True(Radius(end.Motes[0]) > Radius(start.Motes[0]), "L'erreur doit disperser.");
        Assert.Equal(0, end.CoreOffsetX, 6);
        Assert.True(end.AmbientPulse < start.AmbientPulse);

        bool shook = Enumerable.Range(0, 20)
            .Select(i => HypnoticField.Evaluate(HypnoticPreset.Error, period * i / 20.0).CoreOffsetX)
            .Any(offset => Math.Abs(offset) > 0.03);

        Assert.True(shook, "L'erreur doit produire une micro-secousse.");
    }

    [Fact]
    public void Drop_AttractsTowardTheCenter()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Drop);

        // La première particule traverse tout son trajet sur une période : elle
        // doit se rapprocher du centre sans jamais s'en éloigner tant qu'elle est
        // visible.
        double previous = double.MaxValue;

        for (double t = 0.01; t < period * 0.99; t += period / 50)
        {
            HypnoticMote mote = HypnoticField.Evaluate(HypnoticPreset.Drop, t).Motes[0];

            if (mote.Intensity < 0.05)
            {
                continue;
            }

            double radius = Radius(mote);
            Assert.True(radius <= previous + 1e-9, $"La particule s'éloigne à t={t:0.###}");
            previous = radius;
        }
    }

    [Fact]
    public void Sync_FlowsBothWays()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Sync);

        double[] means = Enumerable.Range(0, 16)
            .Select(i => HypnoticField.Evaluate(HypnoticPreset.Sync, period * i / 16.0).Motes.Average(m => m.X))
            .ToArray();

        Assert.Contains(means, mean => mean > 0.2);
        Assert.Contains(means, mean => mean < -0.2);
    }

    [Fact]
    public void Search_SweepsInOneDirection()
    {
        double period = HypnoticField.PeriodSeconds(HypnoticPreset.Search);

        HypnoticMote early = HypnoticField.Evaluate(HypnoticPreset.Search, period * 0.2).Motes[0];
        HypnoticMote later = HypnoticField.Evaluate(HypnoticPreset.Search, period * 0.6).Motes[0];

        Assert.True(later.X > early.X, "La recherche balaie de gauche à droite.");
    }

    [Fact]
    public void None_DoesNotMove()
    {
        Assert.Equal(HypnoticField.Rest, HypnoticField.Evaluate(HypnoticPreset.None, 0));
        Assert.Equal(HypnoticField.Rest, HypnoticField.Evaluate(HypnoticPreset.None, 12.5));
        Assert.Equal(HypnoticField.Rest, HypnoticField.StaticFrame(HypnoticPreset.None));
        Assert.Equal(0, HypnoticField.Rest.AmbientPulse);
    }

    [Fact]
    public void ReducedMotion_KeepsTheCompositionVisible()
    {
        // Sans animation, la matière reste lisible : au moins une particule
        // allumée pour les boucles, la source toujours présente.
        foreach (HypnoticPreset preset in Loops)
        {
            HypnoticFrame still = HypnoticField.StaticFrame(preset);

            Assert.True(still.CoreIntensity > 0.4);
            Assert.Contains(still.Motes, mote => mote.Intensity > 0.1);
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

    private static double Radius(HypnoticMote mote) => Math.Sqrt((mote.X * mote.X) + (mote.Y * mote.Y));
}
