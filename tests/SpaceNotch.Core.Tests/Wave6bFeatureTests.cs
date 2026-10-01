using System;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Features.Calendar;
using SpaceNotch.Features.Channel;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.Productivity;
using SpaceNotch.Platform.Windows.Calendar;
using SpaceNotch.Platform.Windows.Notifications;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6b côté fonctionnalités : canal, silence de réunion, focus calé, commande de capture.</summary>
public class Wave6bFeatureTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 28, 10, 0, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task AMeetingSoon_OffersQuiet_UntilItsEnd()
    {
        DateTimeOffset now = Start.AddMinutes(-3);
        var activities = new ActivityManager();
        var feature = new MeetingFeature(activities, new EventBus(), reader: null, now: () => now);
        DateTimeOffset? requested = null;
        feature.QuietRequested += until => requested = until;

        feature.Show(new CalendarMeeting("m1", "Point produit", Start, Start.AddMinutes(45), null, null, null));
        IslandActivity soon = Assert.Single(activities.GetActiveActivities());
        Assert.Contains(soon.Actions, a => a.Id == MeetingFeature.QuietAction);

        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(MeetingFeature.ActivityId, MeetingFeature.QuietAction)));
        Assert.Equal(Start.AddMinutes(45), requested);

        // Déjà au calme : la proposition disparaît.
        feature.IsQuiet = () => true;
        feature.Show(feature.Current);
        Assert.DoesNotContain(Assert.Single(activities.GetActiveActivities()).Actions, a => a.Id == MeetingFeature.QuietAction);
    }

    [Fact]
    public void MeetingQuiet_HoldsNotifications_AndSaysUntilWhen()
    {
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => false);

        feature.RefreshQuiet();
        feature.QuietUntil(DateTimeOffset.Now.AddMinutes(30));
        Assert.True(feature.IsQuiet);

        feature.Receive("Slack", "Camille", "tu as 2 min ?");
        IslandActivity moon = Assert.Single(activities.GetActiveActivities(), a => a.Id == NotificationFeature.QuietActivityId);
        Assert.StartsWith("Silence jusqu'à", moon.Title);
        Assert.Equal("1", moon.Metric);

        feature.QuietUntil(null);
        Assert.False(feature.IsQuiet);
        Assert.Contains(activities.GetActiveActivities(), a => a.Id == NotificationFeature.QuietSummaryActivityId);
    }

    [Fact]
    public async Task AnAgent_Thinks_Asks_ThenIsAnswered()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());
        string? asked = null;
        feature.QuestionAsked += id => asked = id;

        feature.Receive(new AgentMessage("claude.1", "Claude Code", "SpaceNotch", null, ChannelState.Working));
        IslandActivity thinking = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ActivityPriority.Normal, thinking.Priority);
        Assert.Empty(thinking.Actions);

        feature.Receive(new AgentMessage("claude.1", "Claude Code", "SpaceNotch", "Bash · dotnet test", ChannelState.Waiting));
        IslandActivity question = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ActivityPriority.High, question.Priority);
        Assert.Equal("Bash · dotnet test", question.Subtitle);
        Assert.Equal([ChannelFeature.AllowAction, ChannelFeature.DenyAction], question.Actions.Select(a => a.Id));
        Assert.Equal(ChannelFeature.Prefix + "claude.1", asked);

        // Sans tube (pas démarrée), la réponse ne part pas : la notch le dit.
        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(question.Id, ChannelFeature.AllowAction)));
        IslandActivity after = Assert.Single(activities.GetActiveActivities());
        Assert.Empty(after.Actions);
        Assert.Equal("Réponds dans le terminal", after.Eyebrow);

        feature.Receive(new ClearMessage("claude.1"));
        Assert.Empty(activities.GetActiveActivities());
    }

    [Fact]
    public void AProgress_ShowsItsSteps()
    {
        var activities = new ActivityManager();
        var feature = new ChannelFeature(activities, new EventBus());

        feature.Receive(new ProgressMessage("build", "Build", "Tests", 2, 4, 0.5, ChannelState.Working));
        IslandActivity build = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Build", build.Title);
        Assert.Equal("Tests", build.Subtitle);
        Assert.Equal("2/4", build.TrailingMetric);
        Assert.Equal(0.375, build.Progress!.Value, 3);
        Assert.Equal([1, 0.5, 0, 0], Assert.IsType<ProgressStepsPayload>(build.Payload).Segments);

        feature.Receive(new ProgressMessage("build", "Build", null, 4, 4, 1, ChannelState.Done));
        IslandActivity done = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("✓", done.TrailingMetric);
        Assert.NotNull(done.Duration);
    }

    [Fact]
    public async Task AFocus_EndsBeforeTheNextMeeting()
    {
        var activities = new ActivityManager();
        await using var pomodoro = new PomodoroFeature(activities, new EventBus());
        DateTimeOffset now = Start.AddMinutes(-20);
        pomodoro.NextMeeting = () => (Start, "Point produit");

        var fit = pomodoro.StartFitted(TimeSpan.FromMinutes(25), now);
        Assert.True(fit.Shortened);
        Assert.Equal(TimeSpan.FromMinutes(18), fit.Duration);
        Assert.Equal(TimeSpan.FromMinutes(18), pomodoro.SessionLength);
        Assert.StartsWith("18 min · avant", Assert.Single(activities.GetActiveActivities()).Subtitle);
        pomodoro.Reset();

        var none = pomodoro.StartFitted(TimeSpan.FromMinutes(25), Start.AddMinutes(-4));
        Assert.Equal(TimeSpan.Zero, none.Duration);
        Assert.False(pomodoro.IsSessionRunning);
    }

    [Theory]
    [InlineData("ocr")]
    [InlineData("texte")]
    [InlineData("Capturer du texte")]
    public void Capture_IsATypedCommand(string query)
    {
        Assert.True(LauncherCommands.TryParse(query, french: true, out LauncherCommand command));
        Assert.Equal(LauncherCommandKind.Capture, command.Kind);
        Assert.True(LauncherCommands.TryRead(command.Target, out LauncherCommandKind kind, out _));
        Assert.Equal(LauncherCommandKind.Capture, kind);
    }
}
