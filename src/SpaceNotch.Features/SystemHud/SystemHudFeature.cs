using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Audio;

namespace SpaceNotch.Features.SystemHud;

/// <summary>
/// Retour visuel système : volume, muet. Déclenché par les notifications Core
/// Audio, jamais par sondage.
/// </summary>
public sealed class SystemHudFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.VolumeHud;

    private const string VolumeActivityId = "feature.hud.volume";

    /// <summary>Durée d'affichage du HUD avant disparition automatique.</summary>
    private static readonly TimeSpan HudLifetime = TimeSpan.FromSeconds(2);

    private readonly CoreAudioVolumeListener _volumeListener;

    public SystemHudFeature(
        IActivityManager activities,
        IEventBus events,
        CoreAudioVolumeListener volumeListener,
        bool isEnabled = true)
        : base(FeatureKey, "Retour volume", activities, events, isEnabled)
    {
        _volumeListener = volumeListener ?? throw new ArgumentNullException(nameof(volumeListener));
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _volumeListener.VolumeChanged += OnVolumeChanged;
        _volumeListener.Start();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _volumeListener.VolumeChanged -= OnVolumeChanged;

        // Annule l'inscription auprès du périphérique audio : c'est le point qui
        // fait qu'une fonctionnalité désactivée ne consomme réellement rien.
        _volumeListener.Stop();
        RemoveActivity(VolumeActivityId);

        return Task.CompletedTask;
    }

    /// <summary>Le glyphe suit le niveau : trois ondes, deux, une.</summary>
    private static string GlyphKeyFor(float volume) => volume switch
    {
        >= 66 => "VolumeHigh",
        >= 33 => "VolumeMedium",
        _ => "VolumeLow"
    };

    private void OnVolumeChanged(float volume, bool isMuted)
    {
        // L'expiration appartient au gestionnaire d'activités : la
        // fonctionnalité n'orchestre plus sa propre disparition. La charge utile
        // est la forme commune des retours système — la scène de volume n'en lit
        // pas d'autre.
        IslandActivity activity = HudActivity.Build(
            VolumeActivityId,
            FeatureKey,
            IslandSceneCatalog.VolumeHud,
            "Volume",
            volume,
            100,
            isMuted ? "VolumeMute" : GlyphKeyFor(volume),
            "Sortie principale",
            HudLifetime,
            isMuted);

        PublishActivity(activity);
        PublishEvent(new VolumeChangedEvent(volume, isMuted));
    }
}
