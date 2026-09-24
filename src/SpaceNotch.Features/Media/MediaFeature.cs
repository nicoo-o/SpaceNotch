using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Media;

namespace SpaceNotch.Features.Media;

/// <summary>
/// Session média Windows (GSMTC) : Spotify, YouTube, Chrome, Edge, lecteur
/// système. Entièrement événementiel, aucun sondage.
/// </summary>
public sealed class MediaFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Media;

    /// <summary>
    /// Identifiant stable : republier la piste en cours remplace la précédente
    /// au lieu d'empiler une entrée par changement de titre ou de progression.
    /// </summary>
    private const string ActivityId = "feature.media.current";

    private readonly WindowsMediaSessionManager _sessionManager;

    public MediaFeature(
        IActivityManager activities,
        IEventBus events,
        WindowsMediaSessionManager sessionManager,
        bool isEnabled = true)
        : base(FeatureKey, "Lecture média", activities, events, isEnabled)
    {
        _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
    }

    public const string PreviousAction = "media.previous";

    public const string PlayPauseAction = "media.playpause";

    public const string NextAction = "media.next";

    public const string SeekAction = "media.seek";

    /// <summary>Session média sous-jacente, exposée pour les diagnostics.</summary>
    public WindowsMediaSessionManager SessionManager => _sessionManager;

    /// <summary>
    /// Exécute les contrôles que la fonctionnalité a déclarés.
    ///
    /// C'est le point qui referme la boucle : la vue a proposé un contrôle, la
    /// demande revient ici par identifiant, et seule cette classe sait quel appel
    /// système lui correspond. Le rendu reste sans connaissance du média.
    /// </summary>
    public override async Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return request.ActionId switch
        {
            PreviousAction => await _sessionManager.SkipPreviousAsync().ConfigureAwait(false),
            NextAction => await _sessionManager.SkipNextAsync().ConfigureAwait(false),
            PlayPauseAction => await _sessionManager.TogglePlayPauseAsync().ConfigureAwait(false),
            SeekAction => await SeekFromRequestAsync(request.Value).ConfigureAwait(false),
            _ => false
        };
    }

    /// <summary>
    /// Déplace la tête de lecture. La valeur arrive sous forme de texte : c'est
    /// le contrat neutre des actions, et c'est ce qui évite au cœur de connaître
    /// le moindre type de média.
    /// </summary>
    private Task<bool> SeekFromRequestAsync(string? value)
    {
        if (!double.TryParse(
                value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double seconds))
        {
            return Task.FromResult(false);
        }

        return _sessionManager.SeekAsync(seconds);
    }

    protected override async Task OnStartAsync(CancellationToken cancellationToken)
    {
        // Abonnement posé ici, et non dans le constructeur : c'est ce qui rend la
        // désactivation réellement effective, puis la réactivation sans doublon.
        _sessionManager.TrackChanged += OnTrackChanged;

        await _sessionManager.InitializeAsync().ConfigureAwait(false);
    }

    protected override Task OnStopAsync()
    {
        _sessionManager.TrackChanged -= OnTrackChanged;
        _sessionManager.Shutdown();
        RemoveActivity(ActivityId);

        return Task.CompletedTask;
    }

    private void OnTrackChanged(object? sender, MediaTrackInfo? track)
    {
        if (track == null || (string.IsNullOrWhiteSpace(track.Title) && string.IsNullOrWhiteSpace(track.Artist)))
        {
            RemoveActivity(ActivityId);
            return;
        }

        var activity = new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Media,
            Title = track.Title,
            Subtitle = track.Artist,
            IconKey = "Music",
            Source = track.AppId,
            State = track.IsPlaying ? IslandActivityState.MediaActive : IslandActivityState.Idle,
            Priority = ActivityPriority.Background,

            // Les contrôles sont déclarés par la fonctionnalité, jamais devinés par
            // le rendu : une autre source média peut exposer ses propres actions
            // sans toucher à l'interface.
            Actions =
            [
                new ActivityAction(PreviousAction, "Piste précédente", "Previous"),
                new ActivityAction(
                    PlayPauseAction,
                    track.IsPlaying ? "Pause" : "Lecture",
                    track.IsPlaying ? "Pause" : "Play",
                    ActivityActionKind.Toggle,
                    IsPrimary: true),
                new ActivityAction(NextAction, "Piste suivante", "Next"),
                new ActivityAction(SeekAction, "Déplacer la lecture", "Seek", ActivityActionKind.Invoke, IsEnabled: true)
            ],

            // La teinte de l'atmosphère est déclarée par la fonctionnalité :
            // l'Island ne devine jamais qu'un média doit colorer son halo.
            Tint = track.Tint,
            Artwork = track.ArtworkBytes,
            Payload = track
        };

        PublishActivity(activity);

        PublishEvent(new MediaChangedEvent(new MediaTrackSnapshot(
            track.Title,
            track.Artist,
            track.AlbumTitle,
            track.AppId,
            track.IsPlaying,
            track.Position,
            track.Duration)));
    }
}
