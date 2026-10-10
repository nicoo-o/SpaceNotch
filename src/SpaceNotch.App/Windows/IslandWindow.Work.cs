using System;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Capture;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Channel;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Capture;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Vague 6b : le canal local (agents IA, progressions), le silence de
/// réunion, le focus calé sur l'agenda et la capture de texte. Chacune vit
/// dans sa fonctionnalité ; ici, seulement le câblage.
/// </summary>
public sealed partial class IslandWindow
{
    private const string CaptureActivityId = "feature.capture.text";

    private ChannelFeature? _channelFeature;
    private bool _capturing;

    private ChannelFeature CreateChannelFeature()
    {
        _channelFeature = new ChannelFeature(_activityManager, _eventBus, _settings.IsFeatureEnabled(ChannelFeature.FeatureKey));

        // Une question d'agent ouvre la notch, même si l'agent a déjà été annoncé.
        _channelFeature.QuestionAsked += id => OnUiThread(() =>
        {
            if (_controller.PresentedActivity?.Id == id)
            {
                _controller.RequestExpand();
            }
        });

        return _channelFeature;
    }

    private void WireWork()
    {
        // Silence de réunion (W2) : la réunion propose, les notifications se taisent.
        _meetingFeature.IsQuiet = () => _notificationFeature.IsQuiet;
        _meetingFeature.QuietRequested += until => _notificationFeature.QuietUntil(until);

        // Focus calé (W3) : le pomodoro connaît le prochain rendez-vous.
        _pomodoroFeature.NextMeeting = () => _meetingFeature.Current is { } meeting
            ? (meeting.Start, meeting.Subject)
            : null;
    }

    // ---- Pixel, avatar des agents (ADR-029) -----------------------------------

    /// <summary>Pixel dans la pastille : ses yeux au repos, un peu agrandis.</summary>
    private const double SignalAvatarScale = 1.1;

    private const double CardAvatarScale = 1.3;

    /// <summary>
    /// Un agent a Pixel pour avatar : quand l'activité porte un <see cref="AgentPayload"/>,
    /// il prend la place du glyphe, de la pochette et de la grille qui tourne.
    /// Toujours, même Pixel éteint au repos : le réglage ne concerne que ses yeux
    /// dans la notch au repos (choix de l'auteur, 2026-10-10).
    /// </summary>
    private void ApplyAvatar(
        IslandActivity activity,
        SpaceNotch_App.Views.PixelAvatarView view,
        double scale,
        SpaceNotch_App.Composition.HypnoticSurface? surface,
        FrameworkElement host,
        FrameworkElement glyph,
        FrameworkElement artwork)
    {
        if (activity.Payload is not AgentPayload agent)
        {
            view.Visibility = Visibility.Collapsed;
            return;
        }

        surface?.SetPreset(HypnoticPreset.None, animate: false);
        host.Visibility = Visibility.Collapsed;
        glyph.Visibility = Visibility.Collapsed;
        artwork.Visibility = Visibility.Collapsed;

        view.AvatarScale = scale;
        view.Animate = UseSpringAnimations() && LoopsShown();
        view.Mood = agent.Mood;
        view.Visibility = Visibility.Visible;
    }

    // ---- Capture de texte (W4) --------------------------------------------

    /// <summary>Fige l'écran, laisse tracer un rectangle, copie le texte lu.</summary>
    private void StartTextCapture()
    {
        if (_capturing)
        {
            return;
        }

        if (!OcrReader.IsAvailable)
        {
            PublishCapture(Lang.T("Capture impossible", "Capture unavailable"), Lang.T("Aucune langue OCR installée dans Windows", "No OCR language installed in Windows"), 0);
            return;
        }

        CollapseByUser("travail");
        _capturing = true;

        try
        {
            if (!CaptureWindow.Open(image => OnUiThread(() => _ = FinishCaptureAsync(image))))
            {
                _capturing = false;
            }
        }
        catch (Exception ex)
        {
            _capturing = false;
            MiniLogger.Log("[CAPTURE] Ouverture impossible", ex);
        }
    }

    private async Task FinishCaptureAsync(ScreenImage? image)
    {
        _capturing = false;

        if (image is null)
        {
            return;
        }

        try
        {
            string text = await OcrReader.ReadAsync(image);

            if (text.Length == 0)
            {
                PublishCapture(Lang.T("Aucun texte trouvé", "No text found"), Lang.T("Essaie un rectangle plus large", "Try a larger rectangle"), 0);
                return;
            }

            var package = new global::Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            global::Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);

            int lines = OcrText.LineCount(text);
            PublishCapture(Lang.T("Texte copié · ", "Text copied · ") + Lang.Count(lines, "ligne", "lignes", "line", "lines"), OcrText.Preview(text), lines);
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[CAPTURE] Lecture impossible", ex);
            PublishCapture(Lang.T("Capture impossible", "Capture failed"), ex.Message, 0);
        }
    }

    private void PublishCapture(string title, string subtitle, int lines)
    {
        _activityManager.PostActivity(new IslandActivity
        {
            Id = CaptureActivityId,
            FeatureId = "feature.capture",
            SceneKey = IslandSceneCatalog.Card,
            Title = title,
            Subtitle = subtitle,
            Source = Lang.T("Capture de texte", "Text capture"),
            IconKey = "Text",
            Metric = lines > 0 ? "✓" : null,
            State = IslandActivityState.Notification,
            MotionState = lines > 0 ? ActivityMotionState.Completing : ActivityMotionState.Idle,
            Priority = ActivityPriority.Normal,
            Duration = TimeSpan.FromSeconds(5)
        });

        // C'est la réponse à un geste de l'utilisateur : elle passe devant tout,
        // Focus compris, le temps de sa durée — l'épingle se lève d'elle-même à
        // l'expiration, et l'activité d'avant revient.
        _activityManager.PinPresentation(CaptureActivityId);
    }

    // ---- Écran de veille (P5) ---------------------------------------------
}
