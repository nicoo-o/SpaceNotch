using System;
using System.Linq;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Sound;
using SpaceNotch.Core.SystemInfo;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 5b : charge, moniteur, égaliseur, commandes, sons, animations d'appareils.</summary>
public class Wave5bCoreTests
{
    [Theory]
    [InlineData("audio", "Headphones")]
    [InlineData("gamepad", "Gamepad")]
    [InlineData("keyboard", "Keyboard")]
    [InlineData("mouse", "Mouse")]
    [InlineData("phone", "Call")]
    [InlineData("other", "Bluetooth")]
    public void DeviceAnimations_EndOnTheRestingIcon(string kind, string icon)
    {
        var frames = DeviceAnimation.Connect(kind);

        Assert.True(frames.Count >= 4);
        Assert.All(frames, f => Assert.Equal(49, f.Length));
        Assert.Equal(PixelGlyphs.Resolve(icon), frames[^1]);

        // Quelque chose bouge : au moins une image diffère de l'icône de repos.
        Assert.Contains(frames, f => !f.SequenceEqual(frames[^1]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 1)]
    [InlineData(50, 2)]
    [InlineData(76, 4)]
    [InlineData(100, 4)]
    public void Charging_FillsToTheRealLevel_ThenShowsTheBolt(int percent, int columns)
    {
        Assert.Equal(columns, DeviceAnimation.FillColumns(percent));
        var frames = DeviceAnimation.Charging(percent);

        Assert.Equal(PixelGlyphs.Resolve("Bolt"), frames[^1]);
        Assert.Equal(1 + (2 * columns) + 1, frames.Count);
    }

    [Fact]
    public void ChargeWatch_AnnouncesOnlyAPlugIn()
    {
        var watch = new ChargeWatch();

        // Déjà branché au lancement : rien.
        Assert.False(watch.Update(true, true, 60));
        Assert.False(watch.Update(true, false, 60));
        Assert.True(watch.Update(true, true, 61));
        Assert.False(watch.Update(true, true, 62));
        Assert.Equal(62, watch.Percent);

        // Sans batterie : jamais.
        var desktop = new ChargeWatch();
        Assert.False(desktop.Update(false, false, 0));
        Assert.False(desktop.Update(false, true, 0));
    }

    [Fact]
    public void CpuWatch_AlertsAfterTwentySeconds_AndClearsWithHysteresis()
    {
        var watch = new CpuWatch();
        var t = DateTimeOffset.UnixEpoch;

        Assert.Equal(CpuVerdict.None, watch.Add(95, t));
        Assert.Equal(CpuVerdict.None, watch.Add(95, t.AddSeconds(10)));

        // Une chute sous le seuil remet le compte à zéro.
        Assert.Equal(CpuVerdict.None, watch.Add(40, t.AddSeconds(12)));
        Assert.Equal(CpuVerdict.None, watch.Add(90, t.AddSeconds(14)));
        Assert.Equal(CpuVerdict.None, watch.Add(90, t.AddSeconds(30)));
        Assert.Equal(CpuVerdict.Alert, watch.Add(90, t.AddSeconds(34)));
        Assert.True(watch.IsAlerting);

        // 78 % : sous le seuil d'alerte mais au-dessus du seuil de retour — elle reste.
        Assert.Equal(CpuVerdict.Ongoing, watch.Add(78, t.AddSeconds(40)));
        Assert.Equal(CpuVerdict.Ongoing, watch.Add(50, t.AddSeconds(42)));
        Assert.Equal(CpuVerdict.Clear, watch.Add(50, t.AddSeconds(50)));
        Assert.False(watch.IsAlerting);
        Assert.Equal(9, watch.History.Count);
    }

    [Fact]
    public void CpuWatch_ComputesUsage_AndProtectsTheSystem()
    {
        Assert.Equal(75, CpuWatch.Percent(idleDelta: 25, kernelDelta: 60, userDelta: 40), 3);
        Assert.Equal(0, CpuWatch.Percent(0, 0, 0));
        Assert.True(CpuWatch.IsProtected("csrss", 600, 10));
        Assert.True(CpuWatch.IsProtected("chrome", 10, 10));
        Assert.False(CpuWatch.IsProtected("chrome", 600, 10));
    }

    [Fact]
    public void Equalizer_RestsInSilence_AndRisesWithTheSound()
    {
        Assert.All(EqualizerBars.Heights(0, 1.3), h => Assert.Equal(EqualizerBars.Floor, h));

        double quiet = EqualizerBars.Heights(0.05, 1.3).Average();
        double loud = EqualizerBars.Heights(0.8, 1.3).Average();
        Assert.True(loud > quiet);
        Assert.All(EqualizerBars.Heights(1, 0.4), h => Assert.InRange(h, EqualizerBars.Floor, 1));

        // Les barres ne bougent pas d'un bloc.
        double[] bars = EqualizerBars.Heights(0.6, 2.0);
        Assert.True(bars.Distinct().Count() > 1);
    }

    [Theory]
    [InlineData("timer 10", "Minuteur 10:00", "cmd:timer:600")]
    [InlineData("minuteur 90s", "Minuteur 01:30", "cmd:timer:90")]
    [InlineData("timer 1h", "Minuteur 1:00:00", "cmd:timer:3600")]
    [InlineData("vol 30", "Volume 30 %", "cmd:volume:30")]
    [InlineData("Volume 100%", "Volume 100 %", "cmd:volume:100")]
    [InlineData("#7fe6ff", "#7FE6FF", "cmd:color:7FE6FF")]
    [InlineData("rgb(127, 230, 255)", "#7FE6FF", "cmd:color:7FE6FF")]
    public void Commands_AreRecognised(string query, string title, string target)
    {
        Assert.True(LauncherCommands.TryParse(query, french: true, out LauncherCommand command));
        Assert.Equal(title, command.Title);
        Assert.Equal(target, command.Target);
        Assert.True(LauncherCommands.TryRead(command.Target, out _, out string value));
        Assert.False(string.IsNullOrEmpty(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("timer")]
    [InlineData("timer 0")]
    [InlineData("timer 2000h")]
    [InlineData("vol 130")]
    [InlineData("7FE6FF")]
    [InlineData("chrome")]
    [InlineData("volumes")]
    public void Commands_LeaveOrdinarySearchesAlone(string query)
        => Assert.False(LauncherCommands.TryParse(query, french: true, out _));

    [Theory]
    [InlineData(SoundCueKind.Open)]
    [InlineData(SoundCueKind.Drop)]
    [InlineData(SoundCueKind.TimerDone)]
    public void Sounds_AreShortSoftAndClickFree(SoundCueKind kind)
    {
        float[] samples = SoundCue.Samples(kind);

        Assert.InRange(samples.Length / (double)SoundCue.SampleRate, 0.05, 0.8);
        Assert.InRange(samples.Max(Math.Abs), SoundCue.Peak - 0.001, SoundCue.Peak + 0.001);

        // Début et fin à zéro : pas de claquement.
        Assert.True(Math.Abs(samples[0]) < 0.01);
        Assert.True(Math.Abs(samples[^1]) < 0.01);

        byte[] wav = SoundCue.Wav(kind);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        Assert.Equal(44 + (samples.Length * 2), wav.Length);
    }
}
