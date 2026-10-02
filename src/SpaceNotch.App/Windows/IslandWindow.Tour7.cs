using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Visite de la vague 7 : pour chaque fonction, le repos avec les yeux, puis
/// l'activité qui arrive (les yeux deviennent un morceau d'elle), puis son
/// départ (le morceau redevient les yeux).
/// </summary>
public sealed partial class IslandWindow
{
    private IEnumerable<(string Label, Action Run)> Wave7Tour(Func<IslandActivity> music, Func<IslandActivity> volume)
    {
        string? shown = null;

        IslandActivity Card(string id, string icon, string title, string? eyebrow, string? subtitle, ActivityTint? tint = null, object? payload = null,
            string? metric = null, ActivityMotionState motion = ActivityMotionState.Idle, HypnoticPreset preset = HypnoticPreset.None,
            IReadOnlyList<ActivityAction>? actions = null, ActivityPriority priority = ActivityPriority.Normal, string scene = IslandSceneCatalog.Card)
            => new()
            {
                CreatedAt = DateTimeOffset.UtcNow,
                Id = id,
                FeatureId = TourFeature,
                SceneKey = scene,
                Title = title,
                Eyebrow = eyebrow,
                Subtitle = subtitle,
                IconKey = icon,
                Tint = tint,
                Payload = payload,
                Metric = metric,
                MotionState = motion,
                MotionPreset = preset,
                Actions = actions ?? [],
                Priority = priority,
                State = IslandActivityState.Notification
            };

        // Le repos, puis l'arrivée, puis le départ : 4 s par fonction.
        (string, Action) Pass(string label, Func<IslandActivity> make) => ($"passage · {label}", () =>
        {
            if (shown is not null)
            {
                TourClear(shown);
            }

            _settings.ShowPixel = true;
            _pixelNight = new TimeOnly(14, 32);
            _pixelLook = null;
            IslandActivity activity = make();
            shown = activity.Id;
            TourLater(900, () => TourShow(activity, open: activity.Priority >= ActivityPriority.High));
            TourLater(3000, () => TourClear(activity.Id));
        });

        var mint = new ActivityTint(0x7F, 0xE8, 0xB0);
        var lilac = new ActivityTint(0xB9, 0xA8, 0xFF);

        yield return ("passage · repos", () =>
        {
            TourClear();
            _settings.ShowPixel = true;
            _pixelNight = new TimeOnly(14, 32);
            Render();
        });

        yield return Pass("musique", music);
        yield return Pass("volume", volume);
        yield return Pass("notification", () => Card("tour7.notif", "Message", "Marie", "Discord", Lang.T("on se voit à 19 h ?", "see you at 7 pm?"), new ActivityTint(0x8F, 0xA0, 0xFF), metric: "3"));
        yield return Pass("téléchargement", () => Card("tour7.dl", "Download", "Downloading", "ubuntu-24.04-desktop.iso", null, lilac, metric: "62 %", motion: ActivityMotionState.Working, preset: HypnoticPreset.Process));
        yield return Pass("casque", () => Card("tour7.bt", "Headphones", "AirPods Pro", Lang.T("Connecté", "Connected"), null, mint, metric: "84 %", scene: IslandSceneCatalog.Bluetooth));
        yield return Pass("minuteur", () => Card("tour7.timer", "Timer", "07:42", Lang.T("Minuteur", "Timer"), null, new ActivityTint(0xFF, 0xB2, 0x6B)));
        yield return Pass("claude code", () => Card("tour7.clawd", "Agent", "Claude Code", "SpaceNotch", Lang.T("Réfléchit…", "Thinking…"), new ActivityTint(0xB3, 0x9D, 0xFF), new ClawdPayload(ClawdMood.Thinking), "0 s", ActivityMotionState.Working));
        yield return Pass("travail en cours", () => Card("tour7.work", "Info", "Creating prototype", "Read sidebar.tsx · 741 lines", null, lilac, motion: ActivityMotionState.Working, preset: HypnoticPreset.Process));
        yield return Pass("progression", () => Card("tour7.build", "Progress", "Build", Lang.T("Étape 3/4", "Step 3/4"), "Tests", new ActivityTint(0xFF, 0x8F, 0xA3), new ProgressStepsPayload([1, 1, 0.5, 0]), "3/4", ActivityMotionState.Working, HypnoticPreset.Process));
        yield return Pass("appel", () => Card("tour7.call", "Call", Lang.T("Maman", "Mom"), null, Lang.T("Appel entrant · Phone Link", "Incoming call · Phone Link"), mint,
            actions: [new ActivityAction("tour.answer", Lang.T("Répondre", "Answer"), "Call", ActivityActionKind.Invoke, IsPrimary: true, Tone: ActivityActionTone.Positive), new ActivityAction("tour.decline", Lang.T("Refuser", "Decline"), "Close", ActivityActionKind.Invoke, Tone: ActivityActionTone.Negative)],
            priority: ActivityPriority.High));
        yield return Pass("livraison", () => Card("tour7.delivery", "Scooter", "Uber Eats", Lang.T("En route", "On the way"), null, new ActivityTint(0x06, 0xC1, 0x67), metric: "12 min"));
        yield return Pass("discord vocal", () => Card("tour7.voice", "Headphones", "General", Lang.T("Salon vocal", "Voice channel"), null, new ActivityTint(0x8F, 0xA0, 0xFF), new VoicePayload("General", [], false)));
        yield return Pass("rendez-vous", () => Card("tour7.meeting", "Calendar", Lang.T("Point produit", "Product sync"), Lang.T("dans 4 min · Teams", "in 4 min · Teams"), null, new ActivityTint(0x7F, 0xB8, 0xFF), metric: "4 min"));
        yield return Pass("charge", () => Card("tour7.charge", "Bolt", Lang.T("En charge", "Charging"), Lang.T("Chargeur branché", "Charger connected"), null, mint, metric: "64 %"));
        yield return Pass("ne pas déranger", () => Card("tour7.dnd", "Moon", Lang.T("Au calme jusqu'à 11:30", "Quiet until 11:30"), Lang.T("Ne pas déranger", "Do not disturb"), null, new ActivityTint(0xC9, 0xB8, 0xFF)));
        yield return Pass("texte copié", () => Card("tour7.copy", "Text", Lang.T("Texte copié · 3 lignes", "Text copied · 3 lines"), "FACTURE n° 2026-118", null, metric: "✓"));
        yield return Pass("couleur copiée", () => Card("tour7.color", "Palette", "#FF8FA3", Lang.T("Couleur copiée", "Color copied"), null, new ActivityTint(0xFF, 0x8F, 0xA3)));
        yield return Pass("partage", () => Card("tour7.qr", "Qr", "rapport_final.pdf", Lang.T("Scanne avec ton téléphone", "Scan with your phone"), null));
        yield return Pass("moniteur", () => Card("tour7.cpu", "Cpu", "blender", Lang.T("Processeur 93 %", "Processor 93%"), null, new ActivityTint(0xFF, 0x6B, 0x6B), metric: "93 %"));

        yield return ("passage · recherche", () =>
        {
            if (shown is not null)
            {
                TourClear(shown);
                shown = null;
            }

            TourLater(900, OpenLauncher);
            TourLater(1700, () => LauncherSceneView.Type("photoshop"));
            TourLater(3200, _controller.RequestCollapse);
        });

        yield return ("passage · note", () =>
        {
            TourLater(900, OpenNote);
            TourLater(3200, _controller.RequestCollapse);
        });

        // ---- Pixel vivant ----
        void Rest()
        {
            if (shown is not null)
            {
                TourClear(shown);
                shown = null;
            }

            _controller.RequestCollapse();
            _settings.ShowPixel = true;
            _pixelNight = new TimeOnly(14, 32);
            _pixelLook = null;
            Render();
        }

        yield return ("pixel · bonjour", () =>
        {
            Rest();
            _helloStart = DateTime.UtcNow.AddMilliseconds(300);
        });

        yield return ("pixel · batterie faible", () => { _tourCondition = PixelCondition.Tired; PixelTick(); });
        yield return ("pixel · processeur chaud", () => { _tourCondition = PixelCondition.Hot; PixelTick(); });
        yield return ("pixel · hors ligne", () => { _tourCondition = PixelCondition.Offline; PixelTick(); TourLater(2600, () => { _tourCondition = null; PixelTick(); RestEyes.Blink(); }); });

        yield return ("pixel · le soir, un bâillement", () =>
        {
            _pixelNight = new TimeOnly(22, 41);
            PixelTick();
            TourLater(1200, () => { _nextYawn = DateTime.UtcNow; PixelTick(); });
        });

        yield return ("pixel · il bat la mesure", () =>
        {
            _pixelNight = new TimeOnly(14, 32);
            _tourBeat = true;
            TourLater(3400, () => { _tourBeat = false; PixelTick(); });
        });

        yield return ("pixel · coup d'œil vers l'app active", () =>
        {
            _glance = (-PixelGaze.MaxLookX, PixelGaze.MaxLookY);
            _glanceUntil = DateTime.UtcNow + GlanceDuration;
            TourLater(1800, () => { _glance = (PixelGaze.MaxLookX, PixelGaze.MaxLookY); _glanceUntil = DateTime.UtcNow + GlanceDuration; });
        });

        yield return ("pixel · la teinte suit l'heure", () =>
        {
            _pixelNight = new TimeOnly(19, 0);
            TourLater(700, () => _pixelNight = new TimeOnly(20, 30));
            TourLater(1400, () => _pixelNight = new TimeOnly(21, 15));
            TourLater(2100, () => _pixelNight = new TimeOnly(22, 0));
            TourLater(3200, () => _pixelNight = new TimeOnly(7, 30));
        });

        yield return ("pixel · au revoir (verrouillage)", () =>
        {
            _pixelNight = new TimeOnly(14, 32);
            OnSessionChange(SpaceNotch.Platform.Windows.Shell.SessionNotifications.SessionLock);
            TourLater(2400, () => OnSessionChange(SpaceNotch.Platform.Windows.Shell.SessionNotifications.SessionUnlock));
        });

        // ---- L'interface ----
        yield return ("file d'attente · un point par activité", () =>
        {
            _byeStart = DateTime.MinValue;
            _tourCondition = null;
            IslandActivity first = music();
            TourShow(first, open: false);
            shown = first.Id;
            TourLater(800, () => _activityManager.PostActivity(Card("tour7.q1", "Download", "Downloading", "ubuntu.iso", null, lilac, metric: "62 %")));
            TourLater(1600, () => _activityManager.PostActivity(Card("tour7.q2", "Timer", "07:42", Lang.T("Minuteur", "Timer"), null, new ActivityTint(0xFF, 0xB2, 0x6B))));
            TourLater(3000, () => TourClear(first.Id));
        });

        yield return ("aide des gestes · Alt au survol", () =>
        {
            TourClear("tour7.q1", "tour7.q2");
            IslandActivity first = music();
            TourShow(first, open: false);
            shown = first.Id;
            TourLater(900, () => SetGestureHelp(true));
            TourLater(3200, () => SetGestureHelp(false));
        });

        yield return ("annuler · rattraper une notification écartée", () =>
        {
            TourClear(shown ?? string.Empty);
            shown = null;
            _channelFeature?.Receive(new SpaceNotch.Core.Channel.NotifyMessage("tour7-undo", Lang.T("Script terminé", "Script finished"), Lang.T("42 s, aucune erreur", "42 s, no errors"), "build.ps1", @"C:\Windows\win.ini", null, SpaceNotch.Core.Channel.ChannelState.Done));
            TourLater(500, () => TourOpen(SpaceNotch.Features.Channel.ChannelFeature.Prefix + "tour7-undo"));
            TourLater(1700, () => OnSceneActionRequested(this, new IslandActionRequest(SpaceNotch.Features.Channel.ChannelFeature.Prefix + "tour7-undo", SpaceNotch.Features.Channel.ChannelFeature.DismissAction)));
            TourLater(3300, () => TryUndo());
        });

        yield return ("presse-papier · la pile à la molette", () =>
        {
            TourClear(SpaceNotch.Features.Channel.ChannelFeature.Prefix + "tour7-undo");
            _clipboardFeature.AddForTour("dotnet test -c Release", "text");
            _clipboardFeature.AddForTour("#7FE6FF", "text");
            _clipboardFeature.AddForTour("FACTURE n° 2026-118 · 1 240,00 €", "text");
            _clipboardFeature.AddForTour("https://github.com/nicoo-o/SpaceNotch", "link");
            TourLater(500, () => CycleClipStack(-120));
            TourLater(1300, () => CycleClipStack(-120));
            TourLater(2000, () => CycleClipStack(-120));
            TourLater(2800, () => OnSceneActionRequested(this, new IslandActionRequest(SpaceNotch.Features.Clipboard.ClipboardFeature.StackActivityId, SpaceNotch.Features.Clipboard.ClipboardFeature.StackPasteAction)));
        });

        yield return ("passage · fin", () =>
        {
            _clipboardFeature.HideStack();
            _tourCondition = null;
            _tourBeat = false;
            _settings.ShowPixel = false;
            _pixelNight = null;
            Render();
        });
    }
}
