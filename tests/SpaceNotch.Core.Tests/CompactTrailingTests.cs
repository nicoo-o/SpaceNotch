using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.SystemHud;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Forme compacte : un seul élément vivant à droite, décidé hors du rendu.
/// </summary>
public class CompactTrailingTests
{
    private static IslandActivity Activity(Action<IslandActivity>? setup = null)
    {
        var activity = new IslandActivity { Id = "a", FeatureId = "f", SceneKey = IslandSceneCatalog.Card, Title = "Titre" };
        setup?.Invoke(activity);
        return activity;
    }

    [Fact]
    public void AConnectedHeadset_ShowsItsBattery()
    {
        IslandActivity headset = Activity(a => a.Payload = new BluetoothPayload("AirPods Pro", true, 84, "audio"));

        CompactTrailing trailing = CompactTrailing.For(headset);

        Assert.Equal(TrailingKind.Battery, trailing.Kind);
        Assert.Equal(0.84, trailing.Value, 6);
        Assert.Contains("84", CompactTrailing.MetricFor(headset, trailing));
    }

    [Fact]
    public void ADisconnectedHeadset_ShowsNothing()
    {
        IslandActivity headset = Activity(a => a.Payload = new BluetoothPayload("AirPods Pro", false, 84, "audio"));

        Assert.Equal(TrailingKind.None, CompactTrailing.For(headset).Kind);
    }

    [Fact]
    public void Volume_IsALevel_NotARing()
    {
        IslandActivity volume = HudActivity.Build("v", "f", IslandSceneCatalog.VolumeHud, "Volume", 72, 100, "VolumeHigh", "Sortie", TimeSpan.FromSeconds(2));

        CompactTrailing trailing = CompactTrailing.For(volume);

        Assert.Equal(TrailingKind.Level, trailing.Kind);
        Assert.Equal(0.72, trailing.Value, 6);
    }

    [Fact]
    public void PlayingMusic_Dances_WithoutAMetric()
    {
        IslandActivity music = Activity(a => a.State = IslandActivityState.MediaActive);

        CompactTrailing trailing = CompactTrailing.For(music);

        Assert.Equal(TrailingKind.Equalizer, trailing.Kind);
        Assert.Null(CompactTrailing.MetricFor(music, trailing));
    }

    [Fact]
    public void Work_InProgress_IsARing_WithItsPercentage()
    {
        IslandActivity download = Activity(a => a.Progress = 0.62);

        CompactTrailing trailing = CompactTrailing.For(download);

        Assert.Equal(TrailingKind.Ring, trailing.Kind);
        Assert.Contains("62", CompactTrailing.MetricFor(download, trailing));
    }

    [Fact]
    public void AnActivityWithoutMeasure_StaysQuiet()
    {
        IslandActivity plain = Activity(a => a.Metric = "3");

        Assert.Equal(TrailingKind.None, CompactTrailing.For(plain).Kind);
        Assert.Equal(0, CompactTrailing.For(plain).Width);
        Assert.Equal("3", CompactTrailing.MetricFor(plain, CompactTrailing.For(plain)));
    }

    [Theory]
    [InlineData(0, null)]
    [InlineData(1, null)]
    [InlineData(2, "+1")]
    [InlineData(5, "+4")]
    public void TheStack_IsACount_NotDots(int count, string? expected)
        => Assert.Equal(expected, CompactTrailing.StackBadge(count));
}
