using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Assistant;
using SpaceNotch.Core.Localization;
using SpaceNotch.Features.Assistant;
using SpaceNotch.Features.Notifications;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de la vague 6c : l'intelligence. Le résumé au retour du calme, la
/// phrase comprise dans le lanceur, le rappel qui sonne, les actions sur copie.
/// Le modèle est joué par des réponses écrites d'avance : la machine de
/// tournage n'a ni Phi Silica ni clé, et la visite ne doit rien envoyer.
/// </summary>
public sealed partial class IslandWindow
{
    private IEnumerable<(string Label, Action Run)> Wave6cTour()
    {
        static Func<AssistantRequest, Task<string?>> Says(string answer) => _ => Task.FromResult<string?>(answer);

        yield return ("résumé · au retour du calme", () =>
        {
            TourClear();
            _quietOverride = 2;
            _notificationFeature.UserName = "Nicolas";
            _notificationFeature.Ask = Says(Lang.T(
                "Camille attend ta relecture avant 16 h ; le reste peut attendre.",
                "Camille is waiting for your review before 4 pm; the rest can wait."));
            _notificationFeature.QuietUntil(DateTimeOffset.Now.AddMinutes(30));
            _notificationFeature.Receive("Slack", "Camille", Lang.T("Nicolas, tu peux relire la PR avant 16 h ?", "Nicolas, can you review the PR before 4 pm?"));
            _notificationFeature.Receive("Discord", "Lucas", Lang.T("ce soir ?", "tonight?"));
            _notificationFeature.Receive("Discord", "Marie", Lang.T("go 18 h", "6 pm?"));
            _notificationFeature.Receive("Outlook", "Newsletter", Lang.T("Les nouveautés d'octobre", "What's new in October"));
            _notificationFeature.Receive("Slack", "#général", Lang.T("Déploiement terminé", "Deploy finished"));
            TourLater(500, () =>
            {
                _notificationFeature.QuietUntil(null);
                TourOpen(NotificationFeature.QuietSummaryActivityId);
            });
        });

        yield return ("langage naturel · rappel compris", () =>
        {
            TourClear(NotificationFeature.QuietSummaryActivityId);
            _launcherFeature.AskModelName = "Claude";
            OpenLauncher();
            LauncherSceneView.Type(Lang.T("rappelle-moi d'appeler Paul à 17h", "remind me to call Paul at 5pm"));
        });

        yield return ("langage naturel · demander à Claude", ()
            => LauncherSceneView.Type(Lang.T("range mon bureau avant la démo de jeudi", "tidy my desk before Thursday's demo")));

        yield return ("rappel · posé", () =>
        {
            LauncherSceneView.Type(string.Empty);
            TourClear();
            RunAssistantCommand(SpaceNotch.Core.Launcher.LauncherCommandKind.Reminder,
                DateTimeOffset.Now.AddMinutes(50).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture) + "|" + Lang.T("appeler Paul", "call Paul"));
            TourLater(400, () => _activityManager.PinPresentation(ReminderFeature.NextActivityId));
        });

        yield return ("rappel · c'est l'heure", () =>
        {
            TourClear(AssistantActivityId);
            _reminderFeature?.Add(new NaturalIntent(NaturalKind.Reminder, Lang.T("Relire la PR de Camille", "Review Camille's PR"), DateTimeOffset.Now.AddSeconds(1), null, null));
            TourLater(1300, () => _reminderFeature?.Tick());
        });

        yield return ("copie · actions proposées", () =>
        {
            // Le rappel échu (prioritaire) masquerait la proposition, discrète par nature.
            _reminderFeature?.Clear();
            TourClear();

            if (_copyAssistFeature is null)
            {
                return;
            }

            _copyAssistFeature.Ask = Says(Lang.T(
                "Could you send me the final report before Friday? Thanks!",
                "Peux-tu m'envoyer le rapport final avant vendredi ? Merci !"));
            _copyAssistFeature.Offer(Lang.T(
                "Peux-tu m'envoyer le rapport final avant vendredi ? Merci !",
                "Could you send me the final report before Friday? Thanks!"));
            TourOpen(CopyAssistFeature.ActivityId);
        });

        yield return ("copie · traduction copiée", () =>
        {
            if (_copyAssistFeature is null)
            {
                return;
            }

            _ = _copyAssistFeature.HandleActionAsync(new IslandActionRequest(CopyAssistFeature.ActivityId, CopyAssistFeature.ActionPrefix + "translate"));
            TourLater(300, () => TourOpen(CopyAssistFeature.ActivityId));
        });

        yield return ("assistant · fin", () =>
        {
            TourClear(CopyAssistFeature.ActivityId);

            _reminderFeature?.Clear();
            _quietOverride = 0;
            _notificationFeature.UserName = Environment.UserName;
            ApplyAssistant();
        });
    }
}
