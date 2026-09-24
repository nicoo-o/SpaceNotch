using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.SystemHud;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Retours système : un retour bref dans la forme compacte, pas une ouverture.
/// </summary>
public class HudActivityTests
{
    private static IslandActivity Volume(double value, bool muted = false)
        => HudActivity.Build("v", "f", IslandSceneCatalog.VolumeHud, "Volume", value, 100, "Volume", "Sortie", TimeSpan.FromSeconds(2), muted);

    [Fact]
    public void AVolumeChange_IsACompactOverlay_NotAnOpening()
    {
        IslandActivity volume = Volume(65);

        Assert.Equal(IslandPresentationTier.Signal, IslandPresentation.Resolve(volume));
        Assert.Equal(ActivityPresentationPolicy.Temporary, ActivityPolicies.Resolve(volume));
        Assert.True(volume.Priority < ActivityPriority.High, "Un retour système ne doit pas ouvrir la notch de lui-même.");

        // Il recouvre la musique, qui reviendra seule.
        var media = new IslandActivity { Id = "m", FeatureId = "media", SceneKey = IslandSceneCatalog.Media, Title = "Good Days", Priority = ActivityPriority.Background };
        Assert.Equal(ActivityInterruption.Overlay, ActivityPolicies.Decide(media, volume, NotchPresentation.Compact));
    }

    [Fact]
    public void TheValue_IsTheMetric_AndTheLevel()
    {
        IslandActivity volume = Volume(65);

        Assert.Equal(0.65, volume.Progress!.Value, 6);
        Assert.Equal(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{65:0} %"), volume.TrailingMetric);

        HudPayload payload = Assert.IsType<HudPayload>(volume.Payload);
        Assert.Equal(65, payload.Value);
        Assert.Equal(volume.Metric, payload.ValueText);
    }

    [Fact]
    public void Muted_SaysSo_AndEmptiesTheLevel()
    {
        IslandActivity muted = Volume(40, muted: true);

        Assert.Equal("Muet", muted.TrailingMetric);
        Assert.Equal(0, muted.Progress);
    }

    [Fact]
    public void AHud_NeverMovesHypnotically_NorSpeaks()
    {
        IslandActivity volume = Volume(10);

        Assert.Equal(HypnoticPreset.None, HypnoticField.Resolve(volume.MotionState, volume.MotionPreset));
        Assert.Null(Announcement.For(volume, isNew: true));
    }
}
