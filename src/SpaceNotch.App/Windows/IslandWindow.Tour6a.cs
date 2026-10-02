using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.SystemHud;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de la vague 6a : le caractère de la notch. Chaque étape joue son
/// geste toute seule, sans souris ni fichier réel, pour être filmée.
/// </summary>
public sealed partial class IslandWindow
{
    /// <summary>Lance une action un peu plus tard, dans la même étape de visite.</summary>
    private void TourLater(int milliseconds, Action action)
    {
        Microsoft.UI.Dispatching.DispatcherQueueTimer timer = TrackTimer(DispatcherQueue.CreateTimer());
        timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
        timer.IsRepeating = false;

        // Un minuteur que rien ne retient peut être ramassé avant de sonner :
        // la visite les garde jusqu'à ce qu'ils aient joué.
        _tourLater.Add(timer);
        timer.Tick += (_, _) =>
        {
            _tourLater.Remove(timer);

            try
            {
                if (!_isClosed)
                {
                    action();
                }
            }
            catch (Exception ex)
            {
                MiniLogger.Log("[TOUR] geste différé impossible", ex);
            }
        };
        timer.Start();
    }

    private readonly List<Microsoft.UI.Dispatching.DispatcherQueueTimer> _tourLater = [];

    /// <summary>Visite : heure imposée à l'horloge du repos, pour filmer la rémanence.</summary>
    private string? _tourClock;

    private IEnumerable<(string Label, Action Run)> Wave6aTour(Func<IslandActivity> discord)
    {
        DateTimeOffset Now() => DateTimeOffset.UtcNow;

        IslandActivity Card(string id, string title, string eyebrow, string icon, ActivityMotionState motion = ActivityMotionState.Idle, object? payload = null) => new()
        {
            CreatedAt = Now(),
            Id = id,
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Card,
            Title = title,
            Eyebrow = eyebrow,
            IconKey = icon,
            MotionState = motion,
            Payload = payload,
            State = IslandActivityState.Notification,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive
        };

        IslandActivity Heap(long megabytes) => new()
        {
            CreatedAt = Now(),
            Id = "tour.heap",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("Téléchargement", "Downloading"),
            Eyebrow = "setup-blender-4.2.exe",
            Metric = $"{megabytes} Mo",
            Payload = new BytesPayload(megabytes * 1024 * 1024),
            IconKey = "Download",
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = ActivityMotionState.Working,
            MotionPreset = HypnoticPreset.Process
        };

        IslandActivity Alarm() => new()
        {
            CreatedAt = Now(),
            Id = "tour.alarm",
            FeatureId = TourFeature,
            SceneKey = IslandSceneCatalog.Timer,
            Title = Lang.T("Alarme · 07:30", "Alarm · 07:30"),
            Subtitle = Lang.T("Réveil", "Wake up"),
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            Payload = new TimerPayload(TimeSpan.Zero, true, Lang.T("Alarme", "Alarm"))
        };

        yield return ("pixel · le regard suit", () =>
        {
            TourClear("tour.heap");
            _settings.ShowPixel = true;
            _pixelNight = new TimeOnly(14, 32);
            _pixelLook = (-PixelGaze.MaxLookX, 0.5);
            Render();
            TourLater(1500, () => { _pixelLook = (PixelGaze.MaxLookX, 1); PixelTick(); });
            TourLater(2600, () => RestEyes.Blink());
        });

        yield return ("pixel · surpris", () => { _pixelLook = (0, 0); SurprisePixel(); TourLater(1600, () => { _pixelHovered = true; PixelTick(); }); });

        yield return ("repos · survol, les yeux deviennent les deux-points", () =>
        {
            _pixelHovered = false;
            _pixelLook = null;
            _weatherFeature.Inject(new SpaceNotch.Core.Weather.WeatherReport(14.6, 61, true), "Paris");
            PixelTick();
            TourLater(600, _controller.RequestPreview);
            TourLater(3200, _controller.EndPreview);
        });

        yield return ("repos · assoupi, puis réveil en sursaut", () =>
        {
            _tourDoze = true;
            ArmDozeWatch(atRest: true);
            TourLater(3600, () => _tourDoze = false);
        });

        yield return ("pixel · la nuit", () => { _tourDoze = null; _pixelHovered = false; _pixelNight = new TimeOnly(2, 14); PixelTick(); });

        yield return ("goutte · un fichier au-dessus", () =>
        {
            _settings.ShowPixel = false;
            _pixelNight = null;
            _pixelLook = null;
            Render();
            ShowDropTarget();
            TourLater(500, () => HangDrop(_controller.CurrentFootprint.Width * 0.62));
            TourLater(1600, () => HangDrop(_controller.CurrentFootprint.Width * 0.4));
        });

        // Bue, la goutte rend l'étagère (P2) : le fichier, sa taille, le compte.
        yield return ("goutte · bue", () =>
        {
            SwallowDrop();
            FinishDrop();
            TourLater(700, () => TourShow(
                new IslandActivity
                {
                    CreatedAt = Now(),
                    Id = "tour.shelf",
                    FeatureId = TourFeature,
                    SceneKey = IslandSceneCatalog.Card,
                    Title = "rapport.pdf · 2,4 Mo",
                    Eyebrow = Lang.T("Étagère", "Shelf"),
                    IconKey = "Folder",
                    Metric = "1",
                    Tint = new ActivityTint(0x7F, 0xE6, 0xFF),
                    State = IslandActivityState.Notification,
                    Priority = ActivityPriority.Normal,
                    Policy = ActivityPresentationPolicy.Passive
                },
                open: false));
        });

        yield return ("sablier · téléchargement", () =>
        {
            TourClear("tour.shelf");
            TourShow(Heap(18), open: false);
            TourLater(1200, () => _activityManager.PostActivity(Heap(46)));
            TourLater(2400, () => _activityManager.PostActivity(Heap(71)));
        });

        yield return ("glyphes · lecture → pause", () =>
        {
            TourClear("tour.heap");
            TourShow(Card("tour.glyph", "Nuit blanche", Lang.T("Lecture", "Playing"), "Play"), open: false);
            TourLater(950, () => _activityManager.PostActivity(Card("tour.glyph", "Nuit blanche", Lang.T("En pause", "Paused"), "Pause")));
            TourLater(1900, () => _activityManager.PostActivity(Card("tour.glyph", "Nuit blanche", Lang.T("Piste suivante", "Next track"), "Next")));
            TourLater(2850, () => _activityManager.PostActivity(Card("tour.glyph", "Nuit blanche", Lang.T("Ajouté aux favoris", "Added to favourites"), "Check")));
        });

        yield return ("rémanence · horloge", () =>
        {
            TourClear("tour.glyph");
            _settings.ShowClockAtRest = true;
            _tourClock = "12:58";
            TourLater(900, () => IdleClock.Show(_tourClock = "12:58"));
            TourLater(1900, () => IdleClock.Show(_tourClock = "12:59"));
            TourLater(2900, () => IdleClock.Show(_tourClock = "13:00"));
        });

        yield return ("encre · température", () =>
        {
            _weatherFeature.Inject(new SpaceNotch.Core.Weather.WeatherReport(14.6, 61, true), "Paris");
            _tourPreview ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(900), _controller.RequestPreview);
            _tourPreview.Stop();
            _tourPreview.Start();
            TourLater(2200, () => _weatherFeature.Inject(new SpaceNotch.Core.Weather.WeatherReport(16.2, 61, true), "Paris"));
        });

        yield return ("extinction · notification ignorée", () =>
        {
            _controller.EndPreview();
            _settings.ShowClockAtRest = false;
            _tourClock = null;
            TourShow(discord(), open: true);
            TourLater(2300, NotificationSceneView.DismissForTour);
        });

        yield return ("butée · volume au maximum", () =>
        {
            TourClear(NotificationGroups.ActivityIdFor("Discord"), "tour.clipboard");
            TourShow(HudActivity.Build("tour.volume", TourFeature, IslandSceneCatalog.VolumeHud, "Volume", 100, 100, "VolumeHigh", Lang.T("Sortie principale", "Main output"), TimeSpan.FromSeconds(30)), open: true);
            TourLater(1400, BumpContent);
            TourLater(2400, () => { _lastBump = 0; BumpContent(); });
        });

        yield return ("ressort · une alarme rebondit", () =>
        {
            TourClear("tour.volume");
            TourLater(900, () => _activityManager.PostActivity(Alarm()));
        });

        yield return ("erreur · courte secousse", () =>
        {
            TourClear("tour.alarm");
            TourShow(Card("tour.error", Lang.T("Copie impossible", "Copy failed"), Lang.T("Disque D: plein", "Drive D: full"), "Warning", ActivityMotionState.Error), open: false);
        });

        yield return ("identicône · app sans logo", () =>
        {
            TourClear("tour.error");
            _notificationFeature.Receive("build.ps1", Lang.T("Script terminé", "Script finished"), Lang.T("42 s, aucune erreur", "42 s, no errors"));
            TourLater(900, () => TourOpen(NotificationGroups.ActivityIdFor("build.ps1")));
        });
    }

    /// <summary>Visite : la pastille détachée est lancée vers la droite, fort.</summary>
    private void TourThrow()
    {
        if (!UsesFloatingGeometry)
        {
            return;
        }

        // Comme une main qui la reprend : la pastille part de sa place exacte,
        // et la boucle d'images tourne pendant le vol.
        SpaceNotch.Core.Presentation.ScreenRect rest = CurrentPillRect();
        _pillSpring.Snap(rest.CenterX, rest.CenterY);
        Release((2600, -1400));
        HookDetachFrames();
    }
}
