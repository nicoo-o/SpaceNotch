using System;
using System.Linq;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Phone;
using SpaceNotch.Core.Social;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.Phone;
using SpaceNotch.Features.Social;
using SpaceNotch.Platform.Windows.Notifications;
using Xunit;

namespace SpaceNotch.Core.Tests;

/// <summary>Vague 6d côté fonctionnalités : appel, livraison, salle vocale, notifications interceptées.</summary>
public class Wave6dFeatureTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 1, 19, 20, 0, TimeSpan.FromHours(2));

    [Fact]
    public async Task ACall_RingsGreen_ThenCountsItsDuration()
    {
        DateTimeOffset now = Start;
        var activities = new ActivityManager();
        await using var phone = new PhoneFeature(activities, new EventBus(), now: () => now);
        string? opened = null;
        phone.OpenRequested += uri => opened = uri;

        Assert.True(phone.Offer("Lien avec Windows", "Maman", "Appel entrant"));
        IslandActivity ringing = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(PhoneFeature.CallGreen, ringing.Tint);
        Assert.Equal(ActivityPriority.Critical, ringing.Priority);
        Assert.Equal(PhoneFeature.AnswerAction, ringing.Actions[0].Id);

        Assert.True(await phone.HandleActionAsync(new IslandActionRequest(PhoneFeature.CallActivityId, PhoneFeature.AnswerAction)));
        Assert.Equal(PhoneLink.CallingUri, opened);

        now = Start.AddSeconds(42);
        phone.Tick();
        IslandActivity active = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("0:42", active.Metric);
        Assert.True(phone.InCall);

        Assert.True(phone.Offer("Lien avec Windows", "Maman", "Appel terminé"));
        Assert.Empty(activities.GetActiveActivities());
        Assert.False(phone.InCall);
    }

    [Fact]
    public async Task AMissedCall_OffersToCallBack()
    {
        var activities = new ActivityManager();
        await using var phone = new PhoneFeature(activities, new EventBus(), now: () => Start);

        phone.Offer("Phone Link", "Missed call", "Alex");
        IslandActivity missed = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Alex", missed.Title);
        Assert.Equal(PhoneFeature.CallBackAction, Assert.Single(missed.Actions).Id);
    }

    [Fact]
    public async Task ADelivery_MovesThroughItsSteps()
    {
        DateTimeOffset now = Start;
        var activities = new ActivityManager();
        await using var phone = new PhoneFeature(activities, new EventBus(), now: () => now);

        Assert.True(phone.Offer("Uber Eats", "Uber Eats", "Le restaurant prépare ta commande"));
        IslandActivity preparing = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Uber Eats", preparing.Title);
        Assert.Equal("En préparation", preparing.Subtitle);
        Assert.Equal("Scooter", preparing.IconKey);

        now = Start.AddMinutes(10);
        Assert.True(phone.Offer("Uber Eats", "Uber Eats", "Ton livreur est en route, arrivée dans 12 min"));
        IslandActivity onTheWay = Assert.Single(activities.GetActiveActivities());
        DeliveryPayload payload = Assert.IsType<DeliveryPayload>(onTheWay.Payload);
        Assert.Equal(DeliveryStep.OnTheWay, payload.Step);
        Assert.Equal("12 min", onTheWay.Metric);
        Assert.Equal(ActivityPriority.Normal, onTheWay.Priority);

        // Une étape sans heure garde celle déjà connue.
        now = Start.AddMinutes(14);
        phone.Offer("Uber Eats", "Uber Eats", "Ton livreur approche");
        Assert.Equal("8 min", Assert.Single(activities.GetActiveActivities()).Metric);

        phone.Offer("Uber Eats", "Uber Eats", "Ton livreur est arrivé");
        IslandActivity arrived = Assert.Single(activities.GetActiveActivities());
        Assert.Equal(ActivityPriority.High, arrived.Priority);
        Assert.Null(arrived.Metric);
    }

    [Fact]
    public async Task Notifications_UnderstoodByThePhone_AreNotShownTwice()
    {
        var activities = new ActivityManager();
        using var listener = new WindowsNotificationListener();
        await using var phone = new PhoneFeature(activities, new EventBus(), now: () => Start);
        var notifications = new NotificationFeature(activities, new EventBus(), listener, isQuiet: () => false)
        {
            Intercept = phone.Offer
        };

        notifications.RefreshQuiet();
        notifications.Receive("Lien avec Windows", "Maman", "Appel entrant");
        notifications.Receive("Slack", "Camille", "tu as 2 min ?");

        Assert.Equal(2, activities.GetActiveActivities().Count);
        Assert.Contains(activities.GetActiveActivities(), a => a.Id == PhoneFeature.CallActivityId);
    }

    [Fact]
    public async Task TheVoiceRoom_ShowsWhoSpeaks_AndTogglesTheMic()
    {
        var activities = new ActivityManager();
        await using var discord = new DiscordVoiceFeature(activities, new EventBus(), isEnabled: true);
        bool? asked = null;
        discord.SetMute = mute => { asked = mute; return Task.FromResult(true); };

        discord.Show("42", "Général", [new VoiceMember("1", "Lucas", Speaking: true, Muted: false), new VoiceMember("2", "Marie", false, true)], selfMuted: false);
        IslandActivity room = Assert.Single(activities.GetActiveActivities());
        Assert.Equal("Général", room.Title);
        Assert.Null(room.Subtitle);
        Assert.Equal(ActivityLayout.Row, room.Layout);
        Assert.Equal(2, Assert.IsType<VoicePayload>(room.Payload).Members.Count);

        Assert.True(await discord.HandleActionAsync(new IslandActionRequest(DiscordVoiceFeature.ActivityId, DiscordVoiceFeature.MuteAction)));
        Assert.True(asked);

        discord.Show(null, null, [], false);
        Assert.Empty(activities.GetActiveActivities());
    }
}
