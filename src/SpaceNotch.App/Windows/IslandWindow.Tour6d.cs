using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Media;
using SpaceNotch.Core.Phone;
using SpaceNotch.Core.Social;
using SpaceNotch.Features.Calendar;
using SpaceNotch.Features.Phone;
using SpaceNotch.Features.Social;
using SpaceNotch.Platform.Windows.Calendar;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de la vague 6d : le téléphone et les salons. Un appel qui sonne puis
/// qui dure, une livraison qui avance, un salon Discord où l'on parle, les
/// paroles en grand, le miroir avant la réunion. Les notifications et le
/// salon sont joués ; la machine de tournage n'a ni téléphone ni Discord.
/// </summary>
public sealed partial class IslandWindow
{
    private IEnumerable<(string Label, Action Run)> Wave6dTour(Func<double, CalendarMeeting> meeting, Func<IslandActivity> music)
    {
        VoiceMember[] Room(int speaking) =>
        [
            new("101", "Lucas", speaking == 0, false),
            new("102", "Marie", speaking == 1, false),
            new("103", "Thomas", false, true),
            new("104", "Nicolas", speaking == 3, false)
        ];

        yield return ("appel · ça sonne", () =>
        {
            TourClear();
            _phoneFeature?.Offer("Lien avec Windows", Lang.T("Maman", "Mom"), Lang.T("Appel entrant", "Incoming call"));
            TourOpen(PhoneFeature.CallActivityId);
        });

        yield return ("appel · en cours, la durée", () =>
        {
            _phoneFeature?.ShowCall(new PhoneCall(Lang.T("Maman", "Mom"), CallState.Active));
            _controller.RequestCollapse();
            TourLater(2200, () => TourOpen(PhoneFeature.CallActivityId));
        });

        yield return ("livraison · en route", () =>
        {
            _phoneFeature?.Offer("Lien avec Windows", Lang.T("Maman", "Mom"), Lang.T("Appel terminé", "Call ended"));
            TourClear();
            _phoneFeature?.Offer("Uber Eats", "Uber Eats", Lang.T("Le restaurant prépare ta commande", "The restaurant is preparing your order"));
            TourLater(900, () => _phoneFeature?.Offer("Uber Eats", "Uber Eats", Lang.T("Ton livreur est en route, arrivée dans 12 min", "Your courier is on the way, arriving in 12 min")));
            TourLater(1200, () => TourOpen(PhoneFeature.DeliveryActivityId));
        });

        yield return ("livraison · arrivée", ()
            => _phoneFeature?.Offer("Uber Eats", "Uber Eats", Lang.T("Ton livreur est arrivé, il est devant", "Your courier has arrived, they're outside")));

        yield return ("VTC · chauffeur en route", () =>
        {
            _ = _phoneFeature?.HandleActionAsync(new IslandActionRequest(PhoneFeature.DeliveryActivityId, PhoneFeature.DeliveryDoneAction));
            TourClear();
            _phoneFeature?.Offer("Uber", Lang.T("Ton chauffeur est en route", "Your driver is on the way"), Lang.T("Arrivée à " + DateTimeOffset.Now.AddMinutes(4).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " · Toyota Prius grise", "Arriving at " + DateTimeOffset.Now.AddMinutes(4).ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture) + " · grey Toyota Prius"));
            TourOpen(PhoneFeature.DeliveryActivityId);
        });

        yield return ("discord · salon vocal, qui parle", () =>
        {
            _ = _phoneFeature?.HandleActionAsync(new IslandActionRequest(PhoneFeature.DeliveryActivityId, PhoneFeature.DeliveryDoneAction));
            TourClear(PhoneFeature.DeliveryActivityId);

            if (_discordFeature is not { } discord)
            {
                return;
            }

            bool muted = false;
            discord.SetMute = mute =>
            {
                muted = mute;
                discord.Show("42", Lang.T("Général", "General"), Room(1), muted);
                return Task.FromResult(true);
            };
            discord.Show("42", Lang.T("Général", "General"), Room(0), false);
            TourOpen(DiscordVoiceFeature.ActivityId);
            TourLater(1500, () => discord.Show("42", Lang.T("Général", "General"), Room(1), muted));
            TourLater(2800, () => discord.Show("42", Lang.T("Général", "General"), Room(-1), muted));
        });

        yield return ("discord · micro coupé", ()
            => _ = _discordFeature?.HandleActionAsync(new IslandActionRequest(DiscordVoiceFeature.ActivityId, DiscordVoiceFeature.MuteAction)));

        yield return ("paroles · la ligne chantée", () =>
        {
            _discordFeature?.Show(null, null, [], false);
            TourShow(music(), open: true);
            MediaSceneView.SetLyrics(Lyrics.Parse("""
                [01:18.00]I still wanna try, still believe in good days
                [01:22.50]Good days in my mind, safe in my mind
                [01:24.80]Gotta let go of the weight
                [01:27.00]Good days, good days
                [01:29.20]Still believe in good days
                """));
            MediaSceneView.SetNext(SpotifyApi.NextLine([new QueuedTrack("Kill Bill", "SZA")], Lang.French));
            MediaSceneView.SetLiked(false);
            TourLater(2000, () => MediaSceneView.SetLiked(true));
            TourLater(3600, () => MediaSceneView.ShowQueued(true));
        });

        yield return ("miroir · avant la réunion", () =>
        {
            MediaSceneView.SetLyrics(null);
            MediaSceneView.SetNext(null);
            MediaSceneView.SetLiked(null);
            TourClear("tour.media");
            _meetingFeature.Show(meeting(2.5));
            TourOpen(MeetingFeature.ActivityId);
            TourLater(1200, () => OnActionHovered(MeetingFeature.ActivityId, MeetingFeature.JoinAction, entered: true));
        });

        yield return ("téléphone · fin", () =>
        {
            OnActionHovered(MeetingFeature.ActivityId, MeetingFeature.JoinAction, entered: false);
            _meetingFeature.Show(null);
            TourClear(MeetingFeature.ActivityId);

            if (_discordFeature is not null && _discordClient is not null)
            {
                _discordFeature.SetMute = mute => _discordClient.SetMuteAsync(mute);
            }
        });
    }
}
