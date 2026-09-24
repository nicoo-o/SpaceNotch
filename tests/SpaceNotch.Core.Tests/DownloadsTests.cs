using System;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Features.Downloads;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>
/// Téléchargements : chaque navigateur écrit un fichier partiel à sa manière,
/// et la notch doit raconter la même histoire pour tous.
/// </summary>
public class DownloadsTests
{
    private const string Folder = @"C:\Users\u\Downloads\";

    [Theory]
    [InlineData("setup.exe.crdownload", "setup.exe")]
    [InlineData("film.mkv.part", "film.mkv")]
    [InlineData("doc.pdf.opdownload", "doc.pdf")]
    [InlineData("image.png.download", "image.png")]
    [InlineData("Unconfirmed 482913.crdownload", "Téléchargement")]
    public void EveryBrowser_IsRecognised(string file, string expected)
    {
        Assert.True(DownloadTracker.IsTemporary(Folder + file));
        Assert.Equal(expected, DownloadTracker.DisplayNameOf(Folder + file));
    }

    [Fact]
    public void AnOrdinaryFile_IsIgnored()
    {
        var tracker = new DownloadTracker();

        Assert.Equal(DownloadChange.None, tracker.OnWritten(Folder + "notes.txt", 10));
        Assert.Empty(tracker.Active);
    }

    [Fact]
    public void Chromium_ConfirmsItsName_ThenCompletes()
    {
        var tracker = new DownloadTracker();

        Assert.Equal(DownloadChange.Started, tracker.OnWritten(Folder + "Unconfirmed 1.crdownload", 1000));
        Assert.Equal(DownloadChange.Progressed, tracker.OnRenamed(Folder + "Unconfirmed 1.crdownload", Folder + "app.zip.crdownload", 2000));
        Assert.Equal("app.zip", Assert.Single(tracker.Active).DisplayName);
        Assert.Equal(DownloadChange.Progressed, tracker.OnWritten(Folder + "app.zip.crdownload", 5000));
        Assert.Equal(DownloadChange.Completed, tracker.OnRenamed(Folder + "app.zip.crdownload", Folder + "app.zip", 5000));

        Assert.Empty(tracker.Active);
        Assert.Equal(Folder + "app.zip", tracker.LastCompletedPath);
    }

    [Fact]
    public void Firefox_RenamesItsPartFileAtTheEnd()
    {
        var tracker = new DownloadTracker();

        // Firefox crée aussi un fichier final vide : il ne doit pas compter.
        Assert.Equal(DownloadChange.None, tracker.OnWritten(Folder + "film.mkv", 0));
        Assert.Equal(DownloadChange.Started, tracker.OnWritten(Folder + "film.mkv.part", 100));
        Assert.Equal(DownloadChange.Completed, tracker.OnRenamed(Folder + "film.mkv.part", Folder + "film.mkv", 900));
    }

    [Fact]
    public void ADeletedPartialFile_IsACancellation()
    {
        var tracker = new DownloadTracker();

        tracker.OnWritten(Folder + "x.bin.crdownload", 10);

        Assert.Equal(DownloadChange.Cancelled, tracker.OnDeleted(Folder + "x.bin.crdownload"));
        Assert.Equal(DownloadChange.None, tracker.OnDeleted(Folder + "x.bin.crdownload"));
    }

    [Theory]
    [InlineData(512, "512 o")]
    [InlineData(12_400_000, "12,4 Mo")]
    [InlineData(3_000_000_000, "3 Go")]
    public void Sizes_ReadLikeTheFileExplorer(long bytes, string expected)
    {
        var previous = System.Globalization.CultureInfo.CurrentCulture;
        System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");

        try
        {
            Assert.Equal(expected, DownloadTracker.FormatSize(bytes).Replace('\u00A0', ' '));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task TheFeature_TellsTheWholeStory()
    {
        var activities = new ActivityManager();
        var feature = new DownloadsFeature(activities, new EventBus(), () => "absent-folder-for-tests");

        await feature.StartAsync();

        feature.Written(Folder + "app.zip.crdownload", 2_000_000);

        IslandActivity working = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ActivityMotionState.Working, working.MotionState);
        Assert.Equal(HypnoticPreset.Process, working.MotionPreset);
        Assert.Equal("app.zip", working.Eyebrow);
        Assert.Equal(ActivityPresentationPolicy.Passive, ActivityPolicies.Resolve(working));
        Assert.NotNull(working.TrailingMetric);

        feature.Renamed(Folder + "app.zip.crdownload", Folder + "app.zip", 2_000_000);

        IslandActivity done = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ActivityMotionState.Completing, done.MotionState);
        Assert.Equal(HypnoticPreset.Complete, HypnoticField.Resolve(done.MotionState, done.MotionPreset));
        Assert.Equal("app.zip", done.Eyebrow);
        Assert.NotNull(done.Duration);
        Assert.Contains(done.Actions, a => a.Id == DownloadsFeature.OpenAction && a.IsPrimary);

        await feature.DisposeAsync();
    }

    [Fact]
    public async Task ACancelledLastDownload_LeavesNothingBehind()
    {
        var activities = new ActivityManager();
        var feature = new DownloadsFeature(activities, new EventBus(), () => "absent-folder-for-tests");

        await feature.StartAsync();

        feature.Written(Folder + "a.bin.part", 10);
        feature.Deleted(Folder + "a.bin.part");

        Assert.Empty(activities.GetActiveActivities());

        await feature.DisposeAsync();
    }

    // ------------------------------------------------------------------
    // Emplacement trailing, apaisement, annonces
    // ------------------------------------------------------------------

    [Fact]
    public void TheTrailingMetric_IsDeclaredOrDeducedFromProgress()
    {
        var activity = new IslandActivity { Id = "a", FeatureId = "f", SceneKey = "card", Title = "t", Progress = 0.624 };

        Assert.Equal(string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{62:0} %"), activity.TrailingMetric);

        activity.Metric = "24:37";
        Assert.Equal("24:37", activity.TrailingMetric);

        Assert.Null(new IslandActivity { Id = "b", FeatureId = "f", SceneKey = "card", Title = "t" }.TrailingMetric);
    }

    [Fact]
    public void ALongLoop_RestsInCompact_ButNotWhileTheUserLooks()
    {
        TimeSpan longRun = HypnoticAttenuation.Delay + TimeSpan.FromSeconds(1);

        Assert.True(HypnoticAttenuation.ShouldRest(HypnoticPreset.Process, longRun, NotchPresentation.Compact));
        Assert.False(HypnoticAttenuation.ShouldRest(HypnoticPreset.Process, TimeSpan.FromSeconds(3), NotchPresentation.Compact));
        Assert.False(HypnoticAttenuation.ShouldRest(HypnoticPreset.Process, longRun, NotchPresentation.Expanded));
        Assert.False(HypnoticAttenuation.ShouldRest(HypnoticPreset.Process, longRun, NotchPresentation.Preview));
        Assert.False(HypnoticAttenuation.ShouldRest(HypnoticPreset.Complete, longRun, NotchPresentation.Compact));
    }

    [Fact]
    public void Announcements_AreSpokenOnce_AndOnlyACallInterrupts()
    {
        var download = new IslandActivity { Id = "d", FeatureId = "f", SceneKey = "card", Title = "Téléchargé", Eyebrow = "app.zip" };
        var call = new IslandActivity { Id = "c", FeatureId = "f", SceneKey = "card", Title = "Appel", Priority = ActivityPriority.Critical };
        var volume = new IslandActivity { Id = "v", FeatureId = "f", SceneKey = "card", Title = "65 %", State = State.IslandActivityState.SystemHud };

        Announcement? spoken = Announcement.For(download, isNew: true);

        Assert.NotNull(spoken);
        Assert.Equal("Téléchargé, app.zip", spoken.Value.Text);
        Assert.False(spoken.Value.Assertive);

        Assert.True(Announcement.For(call, isNew: true)!.Value.Assertive);
        Assert.Null(Announcement.For(download, isNew: false));
        Assert.Null(Announcement.For(volume, isNew: true));
    }
}
