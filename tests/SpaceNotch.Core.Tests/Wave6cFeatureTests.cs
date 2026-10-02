using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Features.Assistant;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Platform.Windows.Clipboard;
using SpaceNotch.Platform.Windows.Notifications;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6c côté fonctionnalités : rappels, actions sur copie, résumé, commandes en langage naturel.</summary>
public class Wave6cFeatureTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 14, 10, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task AReminder_WaitsQuietly_ThenRings_AndSnoozes()
    {
        DateTimeOffset now = Now;
        var activities = new ActivityManager();
        await using var feature = new ReminderFeature(activities, new EventBus(), folder: null, now: () => now);

        Reminder? set = feature.Add(new NaturalIntent(NaturalKind.Reminder, "appeler Paul", Now.AddMinutes(50), null, null));
        Assert.NotNull(set);
        Assert.Equal(1, feature.Count);

        IslandActivity upcoming = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ReminderFeature.NextActivityId, upcoming.Id);
        Assert.Equal(ActivityPriority.Background, upcoming.Priority);
        Assert.Equal("Rappel dans 50 min", upcoming.Subtitle);
        Assert.Equal("Appeler Paul", upcoming.Title);

        now = Now.AddMinutes(51);
        feature.Tick();

        IslandActivity due = Assert.Single(activities.GetActiveActivities());
        Assert.StartsWith(ReminderFeature.DueActivityPrefix, due.Id);
        Assert.Equal(ActivityPriority.High, due.Priority);
        Assert.Equal(0, feature.Count);

        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(due.Id, ReminderFeature.SnoozeAction)));
        Assert.Equal(1, feature.Count);
        Assert.Equal(ReminderFeature.NextActivityId, Assert.Single(activities.GetActiveActivities()).Id);
    }

    [Fact]
    public async Task ANonReminderIntent_IsRefused()
    {
        var activities = new ActivityManager();
        await using var feature = new ReminderFeature(activities, new EventBus(), folder: null, now: () => Now);

        Assert.Null(feature.Add(new NaturalIntent(NaturalKind.Timer, string.Empty, null, TimeSpan.FromMinutes(5), null)));
        Assert.Empty(activities.GetActiveActivities());
    }

    [Theory]
    [InlineData(59, "59 min")]
    [InlineData(120, "2 h")]
    [InlineData(30, "30 min")]
    [InlineData(0.2, "1 min")]
    public void Countdown_IsShort(double minutes, string expected)
        => Assert.Equal(expected, ReminderFeature.Countdown(TimeSpan.FromMinutes(minutes)));

    [Fact]
    public async Task ACopiedSentence_WithoutModel_OffersOnlyTheReminder()
    {
        var activities = new ActivityManager();
        using var monitor = new ClipboardMonitor();
        await using var feature = new CopyAssistFeature(activities, new EventBus(), monitor, IntPtr.Zero, isEnabled: true, now: () => Now);

        feature.Offer("Peux-tu m'envoyer le rapport avant vendredi ?");

        IslandActivity offer = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(CopyAssistFeature.ActivityId, offer.Id);
        ActivityAction only = Assert.Single(offer.Actions);
        Assert.Equal(CopyAssistFeature.ActionPrefix + "remind", only.Id);
    }

    [Fact]
    public async Task ACopiedSentence_WithModel_OffersMore()
    {
        var activities = new ActivityManager();
        using var monitor = new ClipboardMonitor();
        await using var feature = new CopyAssistFeature(activities, new EventBus(), monitor, IntPtr.Zero, isEnabled: true, now: () => Now)
        {
            Ask = _ => Task.FromResult<string?>("ok")
        };

        feature.Offer("Could you send me the report before Friday?");

        IslandActivity offer = Assert.Single(activities.GetActiveActivities());
        Assert.Contains(offer.Actions, a => a.Id == CopyAssistFeature.ActionPrefix + "translate");
        Assert.Contains(offer.Actions, a => a.Id == CopyAssistFeature.ActionPrefix + "remind");
    }

    [Fact]
    public async Task TheReminderAction_SetsAReminder_WithoutAnyModel()
    {
        var activities = new ActivityManager();
        using var monitor = new ClipboardMonitor();
        await using var reminders = new ReminderFeature(activities, new EventBus(), folder: null, now: () => Now);
        await using var feature = new CopyAssistFeature(activities, new EventBus(), monitor, IntPtr.Zero, isEnabled: true, now: () => Now)
        {
            AddReminder = reminders.Add
        };

        feature.Offer("Peux-tu m'envoyer le rapport avant vendredi ?");
        Assert.True(await feature.HandleActionAsync(new IslandActionRequest(CopyAssistFeature.ActivityId, CopyAssistFeature.ActionPrefix + "remind")));

        Assert.Equal(1, reminders.Count);
        IslandActivity told = Assert.Single(activities.GetActiveActivities(), a => a.Id == CopyAssistFeature.ActivityId);
        Assert.StartsWith("Rappel posé", told.Title);
    }

    [Fact]
    public void LeavingQuiet_SummarizesWhatConcernsMe_First()
    {
        bool quiet = true;
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => quiet) { UserName = "Nicolas" };

        feature.RefreshQuiet();
        feature.Receive("Slack", "Camille", "Nicolas, tu peux relire la PR avant 16 h ?");
        feature.Receive("Discord", "Serveur", "Nouveau message dans #général");
        feature.Receive("Discord", "Serveur", "Nouveau message dans #général");
        quiet = false;
        feature.RefreshQuiet();

        IslandActivity summary = Assert.Single(activities.GetActiveActivities(), a => a.Id == NotificationFeature.QuietSummaryActivityId);
        Assert.Equal("1 message te concerne", summary.Title);
        QuietPayload payload = Assert.IsType<QuietPayload>(summary.Payload);
        Assert.NotNull(payload.Digest);
        Assert.Equal("Slack", Assert.Single(payload.Digest!.Important).App);
        Assert.Equal(3, payload.Total);
    }

    [Fact]
    public async Task AModel_AddsASentence_OverTheRules()
    {
        bool quiet = true;
        var asked = new TaskCompletionSource<AssistantRequest>();
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        var feature = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => quiet)
        {
            Ask = request =>
            {
                asked.TrySetResult(request);
                return Task.FromResult<string?>("Camille attend ta relecture avant 16 h.");
            }
        };

        feature.RefreshQuiet();
        feature.Receive("Slack", "Camille", "tu peux relire la PR ?");
        quiet = false;
        feature.RefreshQuiet();

        AssistantRequest request = await asked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Contains("<notifications>", request.Prompt);

        IslandActivity summary = await WaitFor(activities, a => a.Id == NotificationFeature.QuietSummaryActivityId && a.Subtitle == "Camille attend ta relecture avant 16 h.");
        Assert.Equal("Camille attend ta relecture avant 16 h.", summary.Subtitle);
    }

    [Fact]
    public void ANaturalSentence_BecomesACommandRow()
    {
        Assert.True(LauncherCommands.TryParse("rappelle-moi d'appeler Paul à 17h", french: true, out LauncherCommand command, Now));
        Assert.Equal(LauncherCommandKind.Reminder, command.Kind);
        Assert.Contains("appeler Paul", command.Title);

        Assert.True(LauncherCommands.TryRead(command.Target, out LauncherCommandKind kind, out string value));
        Assert.Equal(LauncherCommandKind.Reminder, kind);
        Assert.EndsWith("|appeler Paul", value);
    }

    [Fact]
    public void AskTargets_RoundTrip()
    {
        LauncherCommand ask = LauncherCommands.AskCommand("range mon bureau demain", "Claude", french: true);
        Assert.Equal("Demander à Claude", ask.Title);
        Assert.True(LauncherCommands.TryRead(ask.Target, out LauncherCommandKind kind, out string value));
        Assert.Equal(LauncherCommandKind.Ask, kind);
        Assert.Equal("range mon bureau demain", value);
    }

    private static async Task<IslandActivity> WaitFor(ActivityManager activities, Func<IslandActivity, bool> match)
    {
        for (int i = 0; i < 100; i++)
        {
            if (activities.GetActiveActivities().FirstOrDefault(match) is { } found)
            {
                return found;
            }

            await Task.Delay(20);
        }

        throw new TimeoutException("Activité attendue jamais publiée.");
    }
}
