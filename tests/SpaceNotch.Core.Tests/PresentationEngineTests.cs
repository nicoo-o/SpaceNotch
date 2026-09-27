using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Infrastructure.Config;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// La couche de présentation : ce que la notch montre, comment les activités
/// cohabitent, et comment l'atmosphère en découle.
/// </summary>
public class PresentationEngineTests
{
    private static IslandActivity Activity(
        string id,
        ActivityPriority priority = ActivityPriority.Normal,
        TimeSpan? duration = null,
        ActivityPresentationPolicy? policy = null) => new()
        {
            Id = id,
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Card,
            Title = id,
            Priority = priority,
            Duration = duration,
            Policy = policy
        };

    // ------------------------------------------------------------------
    // Présentation
    // ------------------------------------------------------------------

    [Fact]
    public void NoActivity_IsHidden()
    {
        Assert.Equal(NotchPresentation.Hidden, NotchPresentationResolver.Resolve(IslandState.Closed, null));
    }

    [Fact]
    public void AnActivityAtRest_IsCompact()
    {
        Assert.Equal(NotchPresentation.Compact, NotchPresentationResolver.Resolve(IslandState.Closed, Activity("a")));
        Assert.Equal(NotchPresentation.Compact, NotchPresentationResolver.Resolve(IslandState.Collapsing, Activity("a")));
    }

    [Fact]
    public void HoverPreviews_AndOnlyTheClickExpands()
    {
        Assert.Equal(NotchPresentation.Preview, NotchPresentationResolver.Resolve(IslandState.Preview, Activity("a")));
        Assert.Equal(NotchPresentation.Expanded, NotchPresentationResolver.Resolve(IslandState.Expanding, Activity("a")));
        Assert.Equal(NotchPresentation.Expanded, NotchPresentationResolver.Resolve(IslandState.Expanded, Activity("a")));
    }

    [Fact]
    public void ThePreviewOfASignal_CarriesTheSecondLine()
    {
        Assert.Equal(IslandPresentationTier.Card, NotchPresentationResolver.PreviewTier(IslandPresentationTier.Signal));
        Assert.Equal(IslandPresentationTier.Card, NotchPresentationResolver.PreviewTier(IslandPresentationTier.Card));
        Assert.Equal(IslandPresentationTier.Idle, NotchPresentationResolver.PreviewTier(IslandPresentationTier.Idle));
    }

    // ------------------------------------------------------------------
    // Politiques
    // ------------------------------------------------------------------

    [Fact]
    public void Policies_AreDeducedFromUrgencyAndLifetime()
    {
        Assert.Equal(ActivityPresentationPolicy.Persistent, ActivityPolicies.Resolve(Activity("media", ActivityPriority.Background)));
        Assert.Equal(ActivityPresentationPolicy.Temporary, ActivityPolicies.Resolve(Activity("volume", ActivityPriority.Normal, TimeSpan.FromSeconds(2))));
        Assert.Equal(ActivityPresentationPolicy.Passive, ActivityPolicies.Resolve(Activity("download")));
        Assert.Equal(ActivityPresentationPolicy.Interrupting, ActivityPolicies.Resolve(Activity("call", ActivityPriority.Critical)));
    }

    [Fact]
    public void ADeclaredPolicy_Wins()
    {
        IslandActivity download = Activity("download", ActivityPriority.Background, policy: ActivityPresentationPolicy.Passive);

        Assert.Equal(ActivityPresentationPolicy.Passive, ActivityPolicies.Resolve(download));
    }

    [Fact]
    public void AVolumeChange_OverlaysTheMusic_WithoutDestroyingIt()
    {
        IslandActivity media = Activity("media", ActivityPriority.Background);
        IslandActivity volume = Activity("volume", ActivityPriority.Normal, TimeSpan.FromSeconds(2));

        Assert.Equal(ActivityInterruption.Overlay, ActivityPolicies.Decide(media, volume, NotchPresentation.Compact));
    }

    [Fact]
    public void AnIncomingCall_Interrupts_EvenWhenExpanded()
    {
        IslandActivity media = Activity("media", ActivityPriority.Background);
        IslandActivity call = Activity("call", ActivityPriority.Critical);

        Assert.Equal(ActivityInterruption.Interrupt, ActivityPolicies.Decide(media, call, NotchPresentation.Expanded));
        Assert.Equal(ActivityInterruption.Interrupt, ActivityPolicies.Decide(null, call, NotchPresentation.Hidden));
    }

    [Fact]
    public void WhatTheUserIsLookingAt_IsNotReplacedUnderThePointer()
    {
        IslandActivity media = Activity("media", ActivityPriority.Background);
        IslandActivity download = Activity("download");

        Assert.Equal(ActivityInterruption.Queue, ActivityPolicies.Decide(media, download, NotchPresentation.Expanded));
        Assert.Equal(ActivityInterruption.Replace, ActivityPolicies.Decide(media, download, NotchPresentation.Compact));
    }

    [Fact]
    public void ARepublication_IsAnUpdate()
    {
        IslandActivity first = Activity("weather");
        IslandActivity again = Activity("weather");

        Assert.Equal(ActivityInterruption.Replace, ActivityPolicies.Decide(first, again, NotchPresentation.Expanded));
        Assert.Equal(ActivityInterruption.Ignore, ActivityPolicies.Decide(first, null, NotchPresentation.Compact));
    }

    [Fact]
    public void ALessUrgentPassiveActivity_Waits()
    {
        IslandActivity reminder = Activity("reminder", ActivityPriority.High);
        IslandActivity sync = Activity("sync", ActivityPriority.Background, policy: ActivityPresentationPolicy.Passive);

        Assert.Equal(ActivityInterruption.Queue, ActivityPolicies.Decide(reminder, sync, NotchPresentation.Compact));
    }

    // ------------------------------------------------------------------
    // Atmosphère
    // ------------------------------------------------------------------

    [Fact]
    public void WithoutActivity_TheAtmosphereIsNeutral()
    {
        Assert.Equal(AmbientState.Neutral, AmbientState.For(null, ActivityTint.Default, highContrast: false));
    }

    [Fact]
    public void Work_MakesTheAtmosphereMorePresent_AndBreathe()
    {
        IslandActivity idle = Activity("download");
        IslandActivity working = Activity("download");
        working.MotionState = ActivityMotionState.Working;

        AmbientState rest = AmbientState.For(idle, ActivityTint.Default, highContrast: false);
        AmbientState busy = AmbientState.For(working, ActivityTint.Default, highContrast: false);

        Assert.True(busy.Intensity > rest.Intensity);
        Assert.True(busy.Pulse > 0);
        Assert.Equal(0, rest.Pulse);
    }

    [Fact]
    public void HighContrast_KeepsTheAtmosphereNeutralAndStill()
    {
        IslandActivity working = new()
        {
            Id = "a",
            FeatureId = "test",
            SceneKey = IslandSceneCatalog.Card,
            Title = "a",
            Tint = new ActivityTint(200, 40, 200),
            MotionState = ActivityMotionState.Working
        };

        AmbientState ambient = AmbientState.For(working, ActivityTint.Default, highContrast: true);

        Assert.Equal(ActivityTint.Default, ambient.Tint);
        Assert.Equal(0, ambient.Pulse);
    }

    // ------------------------------------------------------------------
    // Mouvement
    // ------------------------------------------------------------------

    [Fact]
    public void MotionStyles_AreOrderedFromCalmToLively()
    {
        var quiet = MotionPresets.Spring(MotionStyle.Quiet);
        var natural = MotionPresets.Spring(MotionStyle.Natural);
        var dynamic = MotionPresets.Spring(MotionStyle.Dynamic);

        Assert.True(quiet.DampingRatio > natural.DampingRatio);
        Assert.True(natural.DampingRatio > dynamic.DampingRatio);
        Assert.True(quiet.ResponseSeconds > dynamic.ResponseSeconds);
    }

    [Fact]
    public void ApplyingAStyle_SetsTheSpring_AndCustomLeavesItAlone()
    {
        var settings = new AppSettings();

        settings.ApplyMotionStyle(MotionStyle.Quiet);

        Assert.Equal(MotionPresets.Spring(MotionStyle.Quiet).ResponseSeconds, settings.SpringResponseSeconds, 3);
        Assert.Equal(MotionPresets.Spring(MotionStyle.Quiet).DampingRatio, settings.SpringBounce, 3);

        settings.SpringBounce = 0.9;
        settings.ApplyMotionStyle(MotionStyle.Custom);

        Assert.Equal(0.9, settings.SpringBounce, 3);
        Assert.Equal(MotionStyle.Custom, settings.MotionStyle);
    }

    [Fact]
    public void AHandTunedSpring_IsNotPassedOffAsAPreset()
    {
        // Une configuration réglée à la main avant les préréglages ne doit pas
        // afficher « Naturel » sur un mouvement qui ne l'est pas.
        var settings = new AppSettings { SpringResponseSeconds = 0.30, SpringBounce = 0.90 };

        settings.Sanitize();

        Assert.Equal(MotionStyle.Custom, settings.MotionStyle);

        var untouched = new AppSettings();
        untouched.Sanitize();

        Assert.Equal(MotionStyle.Natural, untouched.MotionStyle);
    }

    [Fact]
    public void ContentTransitions_FinishBeforeTheShape()
    {
        Assert.True(MotionPresets.DurationMs(MotionKind.Standard) < MotionPresets.DurationMs(MotionKind.Spring));
        Assert.True(MotionPresets.DurationMs(MotionKind.Quick) < MotionPresets.DurationMs(MotionKind.Standard));
        Assert.True(MotionPresets.ReducedDurationMs(MotionKind.Spring) <= 160);
    }
}
