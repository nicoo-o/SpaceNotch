using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.SystemHud;

namespace SpaceNotch.Features.Demo;

/// <summary>Une étape du scénario : publier une activité, ou en retirer une.</summary>
/// <param name="At">Instant depuis le début du scénario.</param>
/// <param name="Post">
/// Fabrique de l'activité à publier, appelée <em>au moment de la publication</em>
/// avec l'instant courant — ou <c>null</c>. Une activité fabriquée d'avance
/// porterait l'heure de fabrication, et une durée de vie comptée depuis celle-ci
/// la ferait arriver déjà expirée.
/// </param>
/// <param name="RemoveId">Identifiant à retirer, ou <c>null</c>.</param>
public sealed record DemoStep(TimeSpan At, Func<DateTimeOffset, IslandActivity>? Post, string? RemoveId = null);

/// <summary>
/// Scénario de démonstration — et de torture.
///
/// <para>
/// Le plan demande un test où tout arrive en même temps : une musique, le
/// volume, quatre messages Discord, un téléchargement, un casque Bluetooth, et
/// une activité qui « réfléchit » comme la référence vidéo. L'objectif : aucune
/// impression de chaos. Le même scénario sert deux fois : rejoué par l'App
/// (<c>--demo</c>, ou le menu de la zone de notification) pour juger à l'œil,
/// et rejoué par les tests pour vérifier les règles de cohabitation.
/// </para>
/// </summary>
public static class DemoScenario
{
    public const string FeatureId = "feature.demo";

    public const string MediaId = "demo.media";

    public const string DownloadId = "demo.download";

    public const string ThinkingId = "demo.thinking";

    /// <summary>Les étapes, dans l'ordre chronologique.</summary>
    public static IReadOnlyList<DemoStep> Steps()
    {
        var steps = new List<DemoStep>();
        var notifications = new NotificationGroups();

        void At(double seconds, Func<DateTimeOffset, IslandActivity>? post, string? remove = null)
            => steps.Add(new DemoStep(TimeSpan.FromSeconds(seconds), post, remove));

        // 1. Une musique : persistante, en arrière-plan.
        At(0, now => new IslandActivity
        {
            CreatedAt = now,
            Id = MediaId,
            FeatureId = FeatureId,
            SceneKey = IslandSceneCatalog.Media,
            Title = "Good Days",
            Subtitle = "SZA",
            Source = "Spotify",
            IconKey = "Music",
            State = IslandActivityState.MediaActive,
            Priority = ActivityPriority.Background,
            Tint = new ActivityTint(0x9B, 0x7B, 0xE0)
        });

        // 2. Le volume : un recouvrement bref, trois valeurs rapprochées.
        foreach ((double at, double value) in new[] { (3.0, 62.0), (3.4, 66.0), (3.8, 72.0) })
        {
            At(at, now => HudActivity.Build("demo.volume", FeatureId, IslandSceneCatalog.VolumeHud, "Volume", value, 100, "VolumeHigh", "Sortie principale", TimeSpan.FromSeconds(2), createdAt: now));
        }

        // 3. Quatre messages Discord : un seul groupe qui compte.
        string[] senders = ["Lucas", "Marie", "Thomas", "Alex"];

        foreach (string sender in senders)
        {
            At(7 + Array.IndexOf(senders, sender), now => notifications.Add(FeatureId, "Discord", sender, "Nouveau message", now));
        }

        // 4. Un téléchargement : la grille « Process », la taille qui grandit.
        for (int i = 0; i <= 6; i++)
        {
            string size = string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{(i + 1) * 0.2:0.0} Go");
            At(13 + i, now => Download(now, size, ActivityMotionState.Working, null));
        }

        At(20, now => Download(now, "✓", ActivityMotionState.Completing, TimeSpan.FromSeconds(5), "Téléchargé"));

        // 5. Un casque qui se connecte : une bonne nouvelle discrète.
        At(22, now => new IslandActivity
        {
            CreatedAt = now,
            Id = "demo.bluetooth",
            FeatureId = FeatureId,
            SceneKey = IslandSceneCatalog.Bluetooth,
            Title = "AirPods Pro",
            Subtitle = "Connecté · Batterie 84 %",
            IconKey = "Bluetooth",
            State = IslandActivityState.DeviceActive,
            Priority = ActivityPriority.Normal,
            Duration = TimeSpan.FromSeconds(3)
        });

        // 6. La référence vidéo, rejouée : lire, réfléchir, construire, finir.
        At(26, now => Thinking(now, "Read app-sidebar.tsx · 219 lines", "Thinking", ActivityMotionState.Working, HypnoticPreset.Think));
        At(30, now => Thinking(now, "Read dropdown-menu.tsx · 257 lines", "Reading file", ActivityMotionState.Working, HypnoticPreset.Read));
        At(34, now => Thinking(now, "Read sidebar.tsx · 741 lines", "Creating prototype", ActivityMotionState.Working, HypnoticPreset.Process));
        At(42, now => Thinking(now, "Read sidebar.tsx · 741 lines", "Prototype prêt", ActivityMotionState.Completing, HypnoticPreset.None));
        At(46, null, ThinkingId);

        // 7. La musique s'arrête : la notch retourne à sa lèvre.
        At(50, null, MediaId);

        return steps;
    }

    private static IslandActivity Download(DateTimeOffset now, string metric, ActivityMotionState state, TimeSpan? lifetime, string title = "Téléchargement")
        => new()
        {
            CreatedAt = now,
            Id = DownloadId,
            FeatureId = FeatureId,
            SceneKey = IslandSceneCatalog.Card,
            Title = title,
            Eyebrow = "ubuntu-24.04-desktop.iso",
            Metric = metric,
            IconKey = "Download",
            State = IslandActivityState.DownloadActive,
            Priority = ActivityPriority.Normal,
            Policy = lifetime is null ? ActivityPresentationPolicy.Passive : ActivityPresentationPolicy.Temporary,
            MotionState = state,
            MotionPreset = HypnoticPreset.Process,
            Duration = lifetime
        };

    private static IslandActivity Thinking(DateTimeOffset now, string eyebrow, string title, ActivityMotionState state, HypnoticPreset preset)
        => new()
        {
            CreatedAt = now,
            Id = ThinkingId,
            FeatureId = FeatureId,
            SceneKey = IslandSceneCatalog.Card,
            Title = title,
            Eyebrow = eyebrow,
            IconKey = "Info",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            MotionState = state,
            MotionPreset = preset
        };
}
