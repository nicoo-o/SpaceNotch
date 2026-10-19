using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Fautes répétées (n° 71) : la notch survit à une exception et continue
/// (n° 33). Quand elles se répètent — vingt en une minute —, une partie d'elle
/// ne marche plus : elle le dit une fois, et propose de relancer plutôt que de
/// continuer en silence.
/// </summary>
public sealed partial class IslandWindow
{
    private const string TroubleActivityId = "app.trouble";
    private const string TroubleRestartAction = "app.trouble.restart";
    private const string TroubleLaterAction = "app.trouble.later";

    /// <summary>Appelé par <see cref="Diagnostics.Guard"/>, de n'importe quel fil.</summary>
    private void OnRepeatedFaults()
        => _dispatcherQueue.TryEnqueueSafely(() =>
        {
            if (_isClosed)
            {
                return;
            }

            MiniLogger.Log("[ERREUR] Exceptions répétées : relance proposée");
            ShowTroubleCard(Lang.T("Relancer la remet d'aplomb", "Restarting sets it right"), canRestart: true);
        });

    private void ShowTroubleCard(string subtitle, bool canRestart)
        => _activityManager.PostActivity(new IslandActivity
        {
            Id = TroubleActivityId,
            FeatureId = TroubleActivityId,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("La notch rencontre des erreurs", "The notch is hitting errors"),
            Subtitle = subtitle,
            Source = "SpaceNotch",
            IconKey = "Warning",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Duration = TimeSpan.FromSeconds(30),
            Actions = canRestart
                ?
                [
                    new ActivityAction(TroubleRestartAction, Lang.T("Relancer", "Restart"), "Check", ActivityActionKind.Invoke, IsPrimary: true),
                    new ActivityAction(TroubleLaterAction, Lang.T("Plus tard", "Later"), "Close")
                ]
                : [new ActivityAction(TroubleLaterAction, Lang.T("Compris", "Got it"), "Close", ActivityActionKind.Invoke, IsPrimary: true)]
        });

    /// <summary>Les boutons de la carte : traités ici, aucune fonctionnalité n'en a la charge.</summary>
    private bool HandleTroubleAction(IslandActionRequest request)
    {
        if (!string.Equals(request.ActivityId, TroubleActivityId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(request.ActionId, TroubleRestartAction, StringComparison.Ordinal) && !RestartNotch())
        {
            // Copie de développement ou installation introuvable : rien à relancer d'ici.
            ShowTroubleCard(Lang.T("Quitte SpaceNotch puis rouvre-le", "Quit SpaceNotch, then open it again"), canRestart: false);
            return true;
        }

        _activityManager.RemoveActivity(TroubleActivityId);
        return true;
    }
}
