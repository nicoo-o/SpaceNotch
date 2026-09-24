using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Privacy;
using SpaceNotch.Platform.Windows.Privacy;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Appels et enregistrements, d'après l'indicateur de confidentialité : ce qui
/// donne une bulle à un appel pendant que la musique occupe la notch.
/// </summary>
public class PrivacyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UnixEpoch.AddDays(20000);

    [Theory]
    [InlineData(@"C:#Users#ana#AppData#Local#Discord#app-1.0.9#Discord.exe", "Discord")]
    [InlineData(@"C:#Program Files#obs-studio#bin#64bit#obs64.exe", "Obs64")]
    [InlineData("MSTeams_8wekyb3d8bbwe", "MSTeams")]
    [InlineData("Microsoft.WindowsCamera_8wekyb3d8bbwe", "WindowsCamera")]
    [InlineData("", "Application")]
    public void AppNamesAreReadable(string appId, string expected)
        => Assert.Equal(expected, PrivacyActivities.AppNameOf(appId));

    [Theory]
    [InlineData(@"C:#Users#ana#AppData#Local#Discord#app-1.0.9#Discord.exe", true)]
    [InlineData("MSTeams_8wekyb3d8bbwe", true)]
    [InlineData(@"C:#Program Files#Zoom#bin#Zoom.exe", true)]
    [InlineData(@"C:#Program Files#obs-studio#bin#64bit#obs64.exe", false)]
    [InlineData("Microsoft.WindowsSoundRecorder_8wekyb3d8bbwe", false)]
    public void CallAppsAreRecognised(string appId, bool call)
        => Assert.Equal(call, PrivacyActivities.IsCallApp(appId));

    [Fact]
    public void AMicrophoneInACallAppIsACall()
    {
        IslandActivity activity = Assert.Single(PrivacyActivities.Build(
            [new CapabilityUsage(CapabilityKind.Microphone, @"C:#Apps#Discord#Discord.exe")], Now));

        Assert.Equal(ActivityRole.Call, activity.Role);
        Assert.Equal("Appel en cours", activity.Title);
        Assert.Equal("Discord", activity.Source);
        Assert.Equal(IslandActivityState.CallActive, activity.State);
        Assert.True(SplitPresentation.IsImportant(activity));
    }

    [Fact]
    public void CameraAndMicrophoneInACallAppIsAVideoCall()
    {
        IslandActivity activity = Assert.Single(PrivacyActivities.Build(
        [
            new CapabilityUsage(CapabilityKind.Microphone, "MSTeams_8wekyb3d8bbwe"),
            new CapabilityUsage(CapabilityKind.Camera, "MSTeams_8wekyb3d8bbwe")
        ], Now));

        Assert.Equal("Appel vidéo", activity.Title);
        Assert.Equal("Call", activity.IconKey);
    }

    [Fact]
    public void AnyOtherUseIsARecording()
    {
        IReadOnlyList<IslandActivity> activities = PrivacyActivities.Build(
        [
            new CapabilityUsage(CapabilityKind.Microphone, @"C:#obs#obs64.exe"),
            new CapabilityUsage(CapabilityKind.Camera, "Microsoft.WindowsCamera_8wekyb3d8bbwe")
        ], Now);

        Assert.Equal(2, activities.Count);
        Assert.All(activities, a => Assert.Equal(ActivityRole.Recording, a.Role));
        Assert.Contains(activities, a => a.Title == "Caméra active");
        Assert.Contains(activities, a => a.Title == "Micro actif");
    }

    [Fact]
    public void NothingInUseMeansNothingPublished()
        => Assert.Empty(PrivacyActivities.Build([], Now));

    [Fact]
    public async Task TheFeaturePublishesAndRetractsAsSensorsAreTakenAndReleased()
    {
        var manager = new ActivityManager(() => Now);
        var source = new FakeSource();
        var feature = new PrivacyFeature(manager, new EventBus(), source, clock: () => Now);

        await feature.StartAsync();
        Assert.Equal(0, manager.Count);

        source.Usages = [new CapabilityUsage(CapabilityKind.Microphone, @"C:#Apps#Discord.exe")];
        source.Raise();
        Assert.Equal(ActivityRole.Call, Assert.Single(manager.GetActiveActivities()).Role);

        // Même état relu : rien n'est republié, l'activité garde sa place.
        IslandActivity first = manager.GetActiveActivities()[0];
        source.Raise();
        Assert.Same(first, manager.GetActiveActivities()[0]);

        source.Usages = [];
        source.Raise();
        Assert.Equal(0, manager.Count);

        await feature.DisposeAsync();
        Assert.True(source.Disposed);
    }

    private sealed class FakeSource : ICapabilityUsageSource
    {
        public IReadOnlyList<CapabilityUsage> Usages { get; set; } = [];

        public bool Disposed { get; private set; }

        public event EventHandler? Changed;

        public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

        public IReadOnlyList<CapabilityUsage> Snapshot() => Usages.ToList();

        public void StartWatching()
        {
        }

        public void StopWatching()
        {
        }

        public void Dispose() => Disposed = true;
    }
}
