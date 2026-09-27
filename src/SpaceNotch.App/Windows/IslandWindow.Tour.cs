using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Media;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.SystemHud;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Media;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de tous les états de la notch (<c>--tour</c>) : chaque forme compacte,
/// chaque scène ouverte, l'aperçu, la pile, la bulle, les languettes et la
/// pastille détachée, quatre secondes chacun. Chaque étape s'écrit au journal
/// (<c>[TOUR] 07 · …</c>) : un tournage peut ainsi en tirer une image par état,
/// pour juger l'ensemble à l'œil.
/// </summary>
public sealed partial class IslandWindow
{
    private const string TourFeature = "feature.tour";
    private static readonly TimeSpan TourStep = TimeSpan.FromSeconds(4);

    private List<(string Label, Action Run)> _tourSteps = [];
    private int _tourIndex;
    private DispatcherQueueTimer? _tourTimer;
    private DispatcherQueueTimer? _tourOpen;

    /// <summary>Lance la visite des états.</summary>
    public void StartTour()
    {
        _tourSteps = BuildTour();
        _tourIndex = 0;
        MiniLogger.Log($"[TOUR] visite lancée : {_tourSteps.Count} états");

        _tourTimer ??= CreateOneShotTimer(TourStep, NextTourStep);
        _tourTimer.Interval = TimeSpan.FromSeconds(2);
        _tourTimer.Start();
    }

    private void NextTourStep()
    {
        if (_tourIndex >= _tourSteps.Count)
        {
            MiniLogger.Log("[TOUR] fin");
            return;
        }

        (string label, Action run) = _tourSteps[_tourIndex];
        MiniLogger.Log($"[TOUR] {_tourIndex:00} · {label}");
        _tourIndex++;

        try
        {
            run();
        }
        catch (Exception ex)
        {
            MiniLogger.Log($"[TOUR] étape impossible : {label}", ex);
        }

        _tourTimer!.Interval = TourStep;
        _tourTimer.Start();
    }

    /// <summary>
    /// Montre une activité, compacte ou ouverte. Ouverte, la notch se referme
    /// d'abord : pendant qu'elle est ouverte, une arrivée ordinaire attend la
    /// fermeture (c'est voulu), la visite doit donc refermer puis rouvrir.
    /// </summary>
    private void TourShow(IslandActivity activity, bool open)
    {
        _controller.RequestCollapse();
        _activityManager.PostActivity(activity);
        _activityManager.PinPresentation(activity.Id);

        if (open)
        {
            _tourOpen ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(700), () => _controller.RequestExpand());
            _tourOpen.Stop();
            _tourOpen.Start();
        }
    }

    /// <summary>Ouvre une activité déjà publiée par une fonctionnalité.</summary>
    private void TourOpen(string id)
    {
        _controller.RequestCollapse();
        _activityManager.PinPresentation(id);
        _tourOpen ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(700), () => _controller.RequestExpand());
        _tourOpen.Stop();
        _tourOpen.Start();
    }

    private void TourClear(params string[] ids)
    {
        _activityManager.PinPresentation(null);
        _controller.RequestCollapse();

        foreach (string id in ids)
        {
            _activityManager.RemoveActivity(id);
        }
    }

    private List<(string Label, Action Run)> BuildTour()
    {
        DateTimeOffset Now() => DateTimeOffset.UtcNow;
        var notifications = new NotificationGroups();

        IslandActivity Music() => new()
        {
            CreatedAt = Now(),
            Id = "tour.media",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Media,
            Title = "Good Days",
            Subtitle = "SZA",
            Source = "Spotify",
            IconKey = "Music",
            State = IslandActivityState.MediaActive,
            Priority = ActivityPriority.Background,
            Tint = new ActivityTint(0x9B, 0x7B, 0xE0),
            Actions =
            [
                new ActivityAction(MediaFeature.PreviousAction, Lang.T("Piste précédente", "Previous track"), "Previous"),
                new ActivityAction(MediaFeature.PlayPauseAction, "Pause", "Pause", ActivityActionKind.Toggle, IsPrimary: true),
                new ActivityAction(MediaFeature.NextAction, Lang.T("Piste suivante", "Next track"), "Next"),
                new ActivityAction(MediaFeature.SeekAction, Lang.T("Déplacer la lecture", "Seek"), "Seek", ActivityActionKind.Invoke, IsEnabled: true)
            ],
            Payload = new MediaTrackInfo("Good Days", "SZA", "SOS", "Spotify.exe", true, TimeSpan.FromSeconds(83), TimeSpan.FromSeconds(279), null, new ActivityTint(0x9B, 0x7B, 0xE0))
        };

        IslandActivity Download() => new()
        {
            CreatedAt = Now(),
            Id = "tour.download",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Téléchargement", "Downloading"),
            Eyebrow = "ubuntu-24.04-desktop.iso",
            Progress = 0.62,
            IconKey = "Download",
            Role = ActivityRole.Download,
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = ActivityMotionState.Working,
            MotionPreset = HypnoticPreset.Process
        };

        IslandActivity Headset() => new()
        {
            CreatedAt = Now(),
            Id = "tour.bluetooth",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Bluetooth,
            Title = "AirPods Pro",
            Subtitle = Lang.T("Connecté", "Connected"),
            IconKey = "Headphones",
            State = IslandActivityState.DeviceActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new BluetoothPayload("AirPods Pro", true, 84, "audio")
        };

        IslandActivity Timer() => new()
        {
            CreatedAt = Now(),
            Id = "tour.timer",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Timer,
            Title = "07:42",
            Subtitle = Lang.T("Minuteur", "Timer"),
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new TimerPayload(new TimeSpan(0, 7, 42), true, Lang.T("Minuteur", "Timer"))
        };

        IslandActivity Thinking() => new()
        {
            CreatedAt = Now(),
            Id = "tour.thinking",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Card,
            Title = "Creating prototype",
            Eyebrow = "Read sidebar.tsx · 741 lines",
            IconKey = "Info",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = ActivityMotionState.Working,
            MotionPreset = HypnoticPreset.Process
        };

        IslandActivity Clipboard() => new()
        {
            CreatedAt = Now(),
            Id = "tour.clipboard",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Clipboard,
            Title = Lang.T("3 éléments", "3 items"),
            Subtitle = Lang.T("Presse-papier", "Clipboard"),
            IconKey = "Clipboard",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new ClipboardPayload(
            [
                new ClipboardEntry("c1", Lang.T("Lien", "Link"), "https://github.com/nicoo-o/SpaceNotch", true),
                new ClipboardEntry("c2", Lang.T("Texte", "Text"), "Code wifi invités : 4F7K-22M9", false),
                new ClipboardEntry("c3", Lang.T("Texte", "Text"), "#7FE6FF", false)
            ])
        };

        IslandActivity Downloaded() => new()
        {
            CreatedAt = Now(),
            Id = "tour.download",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Téléchargé", "Downloaded"),
            Eyebrow = "ubuntu-24.04-desktop.iso",
            IconKey = "Check",
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = ActivityMotionState.Completing
        };

        IslandActivity Colour()
        {
            var color = new ColorCode(0x7F, 0xE6, 0xFF);

            return new()
            {
                CreatedAt = Now(),
                Id = "tour.color",
                FeatureId = TourFeature,
                SceneKey = IslandSceneCatalog.Color,
                Title = color.Hex,
                Subtitle = Lang.T("Couleur copiée", "Colour copied"),
                IconKey = "Palette",
                Tint = new ActivityTint(color.R, color.G, color.B),
                State = IslandActivityState.Idle,
                Priority = ActivityPriority.Normal,
                Policy = ActivityPresentationPolicy.Passive,
                Payload = new ColorPayload(color)
            };
        }

        void Quiet()
        {
            _quietOverride = 1;
            _notificationFeature.RefreshQuiet();
            _notificationFeature.Receive("Slack", "Alice", Lang.T("Réunion déplacée à 15 h", "Meeting moved to 3 pm"));
            _notificationFeature.Receive("Slack", "Bob", Lang.T("Déploiement terminé", "Deploy finished"));
            _notificationFeature.Receive("Mail", Lang.T("Facture de septembre", "September invoice"), "…");
            _controller.RequestCollapse();
            _activityManager.PinPresentation(NotificationFeature.QuietActivityId);
        }

        void QuietOver()
        {
            _quietOverride = 2;
            _notificationFeature.RefreshQuiet();
            TourOpen(NotificationFeature.QuietSummaryActivityId);
        }

        IslandActivity Volume() => HudActivity.Build("tour.volume", TourFeature, IslandSceneCatalog.VolumeHud, "Volume", 72, 100, "VolumeHigh", Lang.T("Sortie principale", "Main output"), TimeSpan.FromSeconds(30));

        IslandActivity Discord()
        {
            notifications.Add(TourFeature, "Discord", "Lucas", Lang.T("t'as vu le lien ?", "did you see the link?"), Now());
            notifications.Add(TourFeature, "Discord", "Thomas", Lang.T("go ce soir", "tonight?"), Now());
            return notifications.Add(TourFeature, "Discord", "Marie", Lang.T("on se voit à 18 h ?", "see you at 6 pm?"), Now());
        }

        return
        [
            ("repos", () => TourClear()),
            ("musique · pastille", () => TourShow(Music(), open: false)),
            ("musique · aperçu au survol", () => _controller.RequestPreview()),
            ("musique · ouverte", () => { _controller.EndPreview(); TourShow(Music(), open: true); }),
            ("volume · pastille", () => TourShow(Volume(), open: false)),
            ("volume · ouvert", () => TourShow(Volume(), open: true)),
            ("notification · pastille", () => { TourClear("tour.volume"); TourShow(Discord(), open: false); }),
            ("notification · ouverte", () => _controller.RequestExpand()),
            ("téléchargement · pastille", () => { TourClear(NotificationGroups.ActivityIdFor("Discord")); TourShow(Download(), open: false); }),
            ("téléchargement · ouvert", () => TourShow(Download(), open: true)),
            ("bluetooth · pastille", () => TourShow(Headset(), open: false)),
            ("bluetooth · ouvert", () => TourShow(Headset(), open: true)),
            ("minuteur · pastille", () => TourShow(Timer(), open: false)),
            ("minuteur · ouvert", () => TourShow(Timer(), open: true)),
            ("travail en cours · pastille", () => TourShow(Thinking(), open: false)),
            ("travail en cours · ouvert", () => TourShow(Thinking(), open: true)),
            ("presse-papier · ouvert", () => TourShow(Clipboard(), open: true)),
            ("téléchargement · coche et rayons", () => { TourClear("tour.clipboard", "tour.thinking"); TourShow(Download(), open: false); }),
            ("téléchargement · terminé", () => TourShow(Downloaded(), open: false)),
            ("couleur copiée · ouverte", () => { TourClear("tour.download"); TourShow(Colour(), open: true); }),
            ("note · ouverte", () => { TourClear("tour.color"); OpenNote(); }),
            ("pomodoro · anneau", () => { _noteFeature.Dismiss(); TourClear("tour.timer", "tour.bluetooth"); _pomodoroFeature.Start(TimeSpan.FromSeconds(30)); }),
            ("ne pas déranger · lune", () => { _pomodoroFeature.Reset(); Quiet(); }),
            ("ne pas déranger · résumé", QuietOver),
            ("recherche · ouverte", () => { TourClear(NotificationFeature.QuietSummaryActivityId); OpenLauncher(); }),
            ("menu rapide", () => { _controller.RequestCollapse(); ToggleQuickMenu(); }),
            ("pile · compteur +N", () => { CloseQuickMenu(); TourShow(Timer(), open: false); }),
            ("bulle · deux activités importantes", () => { TourClear("tour.timer", "tour.bluetooth"); _activityManager.PostActivity(Download()); TourShow(Music(), open: false); }),
            ("languette · gauche", () => DockFromMenu(NotchEdge.Left)),
            ("languette · droite", () => DockFromMenu(NotchEdge.Right)),
            ("retour en haut", () => DockFromMenu(NotchEdge.Top)),
            ("pastille détachée · avec bulle", () => DetachFromMenu()),
            ("raccrochée", () => ReattachTo(NotchEdge.Top, 0.5)),
            ("présentation · premier lancement", () => { TourClear("tour.media", "tour.download"); ShowWelcome(); }),
            ("réglages", () => { TourClear(); OpenSettingsWindow(); }),
            ("fin", () => { _settingsWindow?.Close(); _quietOverride = 0; TourClear(); })
        ];
    }
}
