using System;
using System.Threading;
using System.Threading.Tasks;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;
using NotchFlow.Core.Features;
using NotchFlow.Core.Scenes;
using NotchFlow.Core.State;
using NotchFlow.Platform.Windows.Audio;

namespace NotchFlow.Features.SystemHud;

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

    private void OnVolumeChanged(float volume, bool isMuted)
    {
        var activity = new IslandActivity
        {
            Id = VolumeActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.VolumeHud,
            Title = isMuted ? "Muet" : $"Volume {volume:0}%",
            Subtitle = isMuted ? "Audio désactivé" : "Sortie principale",
            Source = "System.Audio",
            IconKey = isMuted ? "VolumeMute" : "Volume",
            State = IslandActivityState.SystemHud,
            Priority = ActivityPriority.High,

            // L'expiration appartient au gestionnaire d'activités : la
            // fonctionnalité n'orchestre plus sa propre disparition.
            Duration = HudLifetime,
            Payload = (Volume: volume, IsMuted: isMuted)
        };

        PublishActivity(activity);
        PublishEvent(new VolumeChangedEvent(volume, isMuted));
    }
}
