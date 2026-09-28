using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Channel;
using SpaceNotch.Core.Localization;
using SpaceNotch.Features.Calendar;
using SpaceNotch.Features.Channel;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Platform.Windows.Calendar;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de la vague 6b : le travail. Un agent qui réfléchit puis demande,
/// une progression à étapes, le silence de réunion, le focus calé, le texte
/// capturé, l'écran de veille. Tout passe par les vraies fonctionnalités ;
/// seul le tube nommé est court-circuité.
/// </summary>
public sealed partial class IslandWindow
{
    private IEnumerable<(string Label, Action Run)> Wave6bTour(Func<double, CalendarMeeting> meeting)
    {
        const string Agent = "claude.5f3a9c21";
        const string AgentActivity = ChannelFeature.Prefix + Agent;
        const string Build = "build";

        AgentMessage Thinking(string? detail = "SpaceNotch") => new(Agent, ClaudeHook.AgentName, detail, null, ChannelState.Working);

        ProgressMessage Step(int step, double fraction, string label, ChannelState state = ChannelState.Working)
            => new(Build, "Build", label, step, 4, fraction, state);

        void Channel(ChannelMessage message) => _channelFeature?.Receive(message);

        yield return ("agent · réfléchit", () =>
        {
            TourClear();
            Channel(Thinking());
            _activityManager.PinPresentation(AgentActivity);
        });

        yield return ("agent · demande l'autorisation", () =>
        {
            Channel(new AgentMessage(Agent, ClaudeHook.AgentName, "SpaceNotch", "Bash · dotnet test -c Release", ChannelState.Waiting));
            TourOpen(AgentActivity);
        });

        yield return ("agent · autorisé, puis terminé", () =>
        {
            // Sans vrai terminal en attente, la réponse est jouée comme l'agent la verrait.
            Channel(Thinking(Lang.T("Autorisé", "Allowed")));
            TourLater(1800, () => Channel(new AgentMessage(Agent, ClaudeHook.AgentName, "SpaceNotch", null, ChannelState.Done)));
        });

        yield return ("progression · étapes 2/4 → 3/4", () =>
        {
            TourClear(AgentActivity);
            Channel(Step(2, 0.35, Lang.T("Tests", "Tests")));
            TourOpen(ChannelFeature.Prefix + Build);
            TourLater(1300, () => Channel(Step(2, 0.9, Lang.T("Tests", "Tests"))));
            TourLater(2500, () => Channel(Step(3, 0.2, Lang.T("Publication", "Publishing"))));
        });

        yield return ("progression · terminée", () => Channel(Step(4, 1, Lang.T("Publication", "Publishing"), ChannelState.Done)));

        yield return ("silence de réunion · proposé", () =>
        {
            TourClear(ChannelFeature.Prefix + Build);

            // La machine de tournage peut être en « Ne pas déranger » : la proposition n'aurait pas lieu.
            _quietOverride = 2;
            _notificationFeature.RefreshQuiet();
            _meetingFeature.Show(meeting(3.2));
            TourOpen(MeetingFeature.ActivityId);
        });

        yield return ("silence de réunion · actif", () =>
        {
            _ = _meetingFeature.HandleActionAsync(new IslandActionRequest(MeetingFeature.ActivityId, MeetingFeature.QuietAction));
            _meetingFeature.Show(null);
            TourLater(600, () => _notificationFeature.Receive("Slack", "Camille", Lang.T("tu as 2 min ?", "got 2 min?")));
            TourLater(1400, () => TourOpen(NotificationFeature.QuietActivityId));
            TourLater(2200, () => _notificationFeature.Receive("Discord", "Lucas", Lang.T("ce soir ?", "tonight?")));
        });

        yield return ("focus calé · avant la réunion", () =>
        {
            _notificationFeature.QuietUntil(null);
            TourClear(NotificationFeature.QuietActivityId, NotificationFeature.QuietSummaryActivityId);

            // La réunion dans vingt minutes : le focus de 25 min devient 18 min.
            CalendarMeeting next = meeting(20.5);
            _pomodoroFeature.NextMeeting = () => (next.Start, next.Subject);
            _pomodoroFeature.StartFitted(TimeSpan.FromMinutes(25));
            TourLater(900, () => _controller.RequestExpand());
        });

        yield return ("capture de texte · copié", () =>
        {
            _pomodoroFeature.Reset();
            _pomodoroFeature.NextMeeting = () => _meetingFeature.Current is { } m ? (m.Start, m.Subject) : null;
            TourClear();
            PublishCapture(
                Lang.T("Texte copié · 3 ligne(s)", "Text copied · 3 line(s)"),
                OcrPreview(),
                3);
            TourLater(700, () => TourOpen(CaptureActivityId));
        });

        yield return ("écran de veille · jeu de la vie", () =>
        {
            TourClear(CaptureActivityId);
            _tourScreensaver = true;
            TourLater(600, () => { _screensaverOn = false; SetScreensaver(true); });
        });

        yield return ("écran de veille · réveil", () =>
        {
            _tourScreensaver = false;
            SetScreensaver(false);
            _quietOverride = 0;
        });
    }

    private static string OcrPreview()
        => SpaceNotch.Core.Capture.OcrText.Preview(SpaceNotch.Core.Capture.OcrText.Join(["FACTURE n° 2026-118", "Montant dû : 1 240,00 €", "Échéance : 30 sept."]));
}
