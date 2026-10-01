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
/// réunion, le focus calé sur l'agenda, la capture de texte et l'écran de
/// veille. Chacune vit dans sa fonctionnalité ; ici, seulement le câblage.
/// </summary>
public sealed partial class IslandWindow
{
    private const string CaptureActivityId = "feature.capture.text";

    private static readonly TimeSpan ScreensaverCheck = TimeSpan.FromSeconds(20);
    private static readonly TimeSpan LifeFrame = TimeSpan.FromMilliseconds(1000.0 / LifeGrid.FramesPerSecond);

    private ChannelFeature? _channelFeature;
    private DispatcherQueueTimer? _screensaverTimer;
    private DispatcherQueueTimer? _lifeTimer;
    private bool _screensaverOn;
    private bool _capturing;

    /// <summary>Visite : l'écran de veille forcé, sans attendre cinq minutes.</summary>
    private bool _tourScreensaver;

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

    private void WireWave6b()
    {
        // Silence de réunion (W2) : la réunion propose, les notifications se taisent.
        _meetingFeature.IsQuiet = () => _notificationFeature.IsQuiet;
        _meetingFeature.QuietRequested += until => _notificationFeature.QuietUntil(until);

        // Focus calé (W3) : le pomodoro connaît le prochain rendez-vous.
        _pomodoroFeature.NextMeeting = () => _meetingFeature.Current is { } meeting
            ? (meeting.Start, meeting.Subject)
            : null;

        ArmScreensaver();
    }

    // ---- Clawd (I4) -------------------------------------------------------

    /// <summary>Un pixel de Clawd dans la pastille : il remplit sa hauteur (≈ 20 DIP).</summary>
    private const double SignalClawdPitch = 1.15;

    private const double CardClawdPitch = 1.3;

    /// <summary>
    /// Claude Code a sa mascotte : quand l'activité porte un <see cref="ClawdPayload"/>,
    /// Clawd prend la place du glyphe, de la pochette et de la grille qui tourne.
    /// </summary>
    private void ApplyClawd(
        IslandActivity activity,
        SpaceNotch_App.Views.ClawdView view,
        double pitch,
        SpaceNotch_App.Composition.HypnoticSurface? surface,
        FrameworkElement host,
        FrameworkElement glyph,
        FrameworkElement artwork)
    {
        if (activity.Payload is not ClawdPayload clawd)
        {
            view.Visibility = Visibility.Collapsed;
            return;
        }

        surface?.SetPreset(HypnoticPreset.None, animate: false);
        host.Visibility = Visibility.Collapsed;
        glyph.Visibility = Visibility.Collapsed;
        artwork.Visibility = Visibility.Collapsed;

        view.Pitch = pitch;
        view.PixelStyle = _settings.ClawdStyle;
        view.Animate = UseSpringAnimations();
        view.Mood = clawd.Mood;
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

        _controller.RequestCollapse();
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
            PublishCapture(Lang.T($"Texte copié · {lines} ligne(s)", $"Text copied · {lines} line(s)"), OcrText.Preview(text), lines);
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
    }

    // ---- Écran de veille (P5) ---------------------------------------------

    /// <summary>
    /// Arme la vérification d'inactivité, seulement si l'écran de veille est
    /// voulu : sinon, rien ne tourne. Une lecture système toutes les vingt
    /// secondes ; le jeu de la vie ne bat qu'une fois affiché.
    /// </summary>
    private void ArmScreensaver()
    {
        _screensaverTimer ??= CreateRepeatingTimer(ScreensaverCheck, CheckScreensaver);

        if (_settings.ShowScreensaver || _tourScreensaver)
        {
            _screensaverTimer.Start();
        }
        else
        {
            _screensaverTimer.Stop();
            SetScreensaver(false);
        }
    }

    private void CheckScreensaver()
    {
        bool onBattery = global::Windows.System.Power.PowerManager.PowerSupplyStatus == global::Windows.System.Power.PowerSupplyStatus.NotPresent;
        bool should = _tourScreensaver || ScreensaverPolicy.ShouldRun(
            _settings.ShowScreensaver,
            IdleProbe.Idle(),
            onBattery,
            _presence.ShouldHide,
            _controller.PresentedActivity is not null);

        SetScreensaver(should);
    }

    private void SetScreensaver(bool on)
    {
        if (on == _screensaverOn)
        {
            return;
        }

        _screensaverOn = on;

        if (on)
        {
            int weather = _weatherFeature.Current?.Code ?? 0;
            RestLife.Seed(LifeGrid.SeedFor(DateOnly.FromDateTime(DateTime.Now), weather));
        }

        Render();
    }

    /// <summary>Appelé par le rendu au repos : la vie remplace les yeux et l'heure tant qu'elle tourne.</summary>
    private void ShowRestLife(bool atRest)
    {
        bool visible = atRest && _screensaverOn && !UsesSideTab;
        RestLife.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        if (!visible)
        {
            _lifeTimer?.Stop();
            return;
        }

        RestEyes.Visibility = Visibility.Collapsed;
        IdleClock.Visibility = Visibility.Collapsed;
        IdleStatusDot.Visibility = Visibility.Collapsed;
        WeatherGlyph.Visibility = Visibility.Collapsed;
        _gazeTimer?.Stop();
        _blinkTimer?.Stop();

        _lifeTimer ??= CreateRepeatingTimer(LifeFrame, LifeTick);
        _lifeTimer.Start();
    }

    private void LifeTick()
    {
        // Le moindre geste réveille la notch : pas d'attente de la prochaine vérification.
        if (!_tourScreensaver && IdleProbe.Idle() < TimeSpan.FromSeconds(1))
        {
            SetScreensaver(false);
            return;
        }

        RestLife.Step();
    }
}
