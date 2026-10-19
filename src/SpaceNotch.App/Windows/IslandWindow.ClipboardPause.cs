using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Indicateur de la pause du presse-papier (n° 3). Il appartient à l'hôte, non à une
/// fonctionnalité : la pause vaut pour l'historique comme pour les actions sur copie, et
/// l'indicateur doit rester tant qu'un des deux est en pause, même si l'autre est éteint.
/// </summary>
public sealed partial class IslandWindow
{
    private const string ClipboardPausedActivityId = "app.clipboard-paused";
    private const string ClipboardResumeAction = "clipboard.resume";

    private void OnClipboardPauseChanged() => OnUiThread(() =>
    {
        if (!_clipboardPause.IsPaused)
        {
            _activityManager.RemoveActivity(ClipboardPausedActivityId);
            return;
        }

        _activityManager.PostActivity(new IslandActivity
        {
            Id = ClipboardPausedActivityId,
            FeatureId = ClipboardPausedActivityId,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Presse-papier en pause", "Clipboard paused"),
            Subtitle = _clipboardPause.Until is { } until
                ? Lang.T("Reprise à ", "Resumes at ") + until.ToLocalTime().ToString("t", System.Globalization.CultureInfo.CurrentCulture)
                : Lang.T("Historique et actions sur copie ne lisent rien", "History and copy actions read nothing"),
            Source = Lang.T("Presse-papier", "Clipboard"),
            IconKey = "Clipboard",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Background,
            Policy = ActivityPresentationPolicy.Passive,
            Actions = [new ActivityAction(ClipboardResumeAction, Lang.T("Reprendre", "Resume"), "Play", ActivityActionKind.Invoke, IsPrimary: true)]
        });
    });

    private bool HandleClipboardPauseAction(IslandActionRequest request)
    {
        if (!string.Equals(request.ActivityId, ClipboardPausedActivityId, StringComparison.Ordinal))
        {
            return false;
        }

        _clipboardPause.Resume();
        return true;
    }
}
