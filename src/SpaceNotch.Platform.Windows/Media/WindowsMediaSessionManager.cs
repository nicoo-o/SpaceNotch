using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using Windows.Media.Control;

namespace SpaceNotch.Platform.Windows.Media;

/// <summary>
/// Gestionnaire natif des sessions multimédia Windows (GSMTC) : Spotify, YouTube, Chrome, Edge, etc.
/// 100% événementiel, 0% CPU au repos.
/// </summary>
public sealed class WindowsMediaSessionManager
{
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;

    /// <summary>
    /// Clé de la piste dont la pochette a déjà été décodée.
    ///
    /// Indispensable : la progression de lecture notifie en continu, et sans ce
    /// cache la pochette serait redécodée à chaque battement. Le décodage ne coûte
    /// donc rien pendant la lecture d'un même morceau, ce qui est exactement la
    /// règle « aucune tâche inutile ».
    /// </summary>
    private string? _artworkCacheKey;

    private byte[]? _cachedArtwork;

    private ActivityTint? _cachedTint;

    public event EventHandler<MediaTrackInfo?>? TrackChanged;
    public event EventHandler<bool>? PlaybackStateChanged;

    public async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                UpdateCurrentSession(_sessionManager.GetCurrentSession());
            }
        }
        catch
        {
            // Tolérance si l'API GSMTC n'est pas disponible dans l'environnement
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateCurrentSession(sender.GetCurrentSession());
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? newSession)
    {
        DetachCurrentSession();

        _currentSession = newSession;

        // Changement de source : la pochette en cache ne décrit plus la session.
        InvalidateArtwork();

        if (_currentSession != null)
        {
            _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged += OnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged += OnTimelinePropertiesChanged;

            RequestRefresh();
        }
        else
        {
            TrackChanged?.Invoke(this, null);
        }
    }

    private void OnMediaPropertiesChanged(GlobalSystemMediaTransportControlsSession sender, MediaPropertiesChangedEventArgs args)
    {
        RequestRefresh();
    }

    private void OnPlaybackInfoChanged(GlobalSystemMediaTransportControlsSession sender, PlaybackInfoChangedEventArgs args)
    {
        var info = sender.GetPlaybackInfo();
        bool isPlaying = info?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        PlaybackStateChanged?.Invoke(this, isPlaying);
        RequestRefresh();
    }

    private void OnTimelinePropertiesChanged(GlobalSystemMediaTransportControlsSession sender, TimelinePropertiesChangedEventArgs args)
    {
        RequestRefresh();
    }

    private int _refreshing;
    private int _refreshAgain;

    /// <summary>
    /// Demande une relecture. Les événements du lecteur arrivent en rafale — la
    /// progression notifie sans cesse — et chacun lançait sa propre lecture : elles
    /// se chevauchaient, et la plus lente, donc la plus ancienne, pouvait écraser la
    /// plus récente. Ici, une seule lecture à la fois ; celles demandées pendant
    /// qu'elle tourne se fondent en une seule, faite juste après.
    /// </summary>
    private void RequestRefresh()
    {
        Interlocked.Exchange(ref _refreshAgain, 1);

        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0)
        {
            return;
        }

        _ = RunRefreshesAsync();
    }

    private async Task RunRefreshesAsync()
    {
        try
        {
            while (Interlocked.Exchange(ref _refreshAgain, 0) == 1)
            {
                await RefreshMediaInfoAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);

            // Une demande arrivée entre la fin de la boucle et la libération.
            if (Volatile.Read(ref _refreshAgain) == 1)
            {
                RequestRefresh();
            }
        }
    }

    public async Task<MediaTrackInfo?> RefreshMediaInfoAsync()
    {
        GlobalSystemMediaTransportControlsSession? session = _currentSession;

        if (session == null)
        {
            TrackChanged?.Invoke(this, null);
            return null;
        }

        try
        {
            var mediaProps = await session.TryGetMediaPropertiesAsync();

            // Le lecteur a changé pendant la lecture : ce résultat décrit
            // l'ancien, la relecture du nouveau suit déjà.
            if (!ReferenceEquals(session, _currentSession))
            {
                return null;
            }

            var playbackInfo = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            if (mediaProps == null)
            {
                TrackChanged?.Invoke(this, null);
                return null;
            }

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;

            string title = string.IsNullOrWhiteSpace(mediaProps.Title) ? "Lecture en cours" : mediaProps.Title;
            string artist = string.IsNullOrWhiteSpace(mediaProps.Artist) ? session.SourceAppUserModelId : mediaProps.Artist;

            await EnsureArtworkAsync(title, artist, mediaProps).ConfigureAwait(false);

            if (!ReferenceEquals(session, _currentSession))
            {
                return null;
            }

            var trackInfo = new MediaTrackInfo(
                Title: title,
                Artist: artist,
                AlbumTitle: mediaProps.AlbumTitle ?? string.Empty,
                AppId: session.SourceAppUserModelId,
                IsPlaying: isPlaying,

                // Position relative à l'instant où elle a été lue : le rendu n'a
                // pas à extrapoler une lecture qui a pu être mise en pause entre
                // l'événement et l'affichage.
                Position: timeline?.Position ?? TimeSpan.Zero,
                Duration: timeline?.EndTime ?? TimeSpan.Zero,
                ArtworkBytes: _cachedArtwork,
                Tint: _cachedTint
            );

            TrackChanged?.Invoke(this, trackInfo);
            return trackInfo;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Décode la pochette et sa teinte si la piste a changé, et réutilise le
    /// résultat sinon.
    /// </summary>
    private async Task EnsureArtworkAsync(string title, string artist, GlobalSystemMediaTransportControlsSessionMediaProperties mediaProps)
    {
        string key = $"{mediaProps.AlbumTitle}|{title}|{artist}";

        if (string.Equals(key, _artworkCacheKey, StringComparison.Ordinal))
        {
            return;
        }

        _artworkCacheKey = key;
        _cachedArtwork = null;
        _cachedTint = null;

        try
        {
            AlbumArtwork artwork = await AlbumPalette.ReadAsync(mediaProps.Thumbnail).ConfigureAwait(false);

            _cachedArtwork = artwork.Bytes;
            _cachedTint = artwork.Tint;
        }
        catch (Exception)
        {
            // Une pochette illisible n'empêche pas la lecture.
        }
    }

    /// <summary>
    /// Oublie la pochette en cache. Appelé lorsque la session change de source.
    /// </summary>
    private void InvalidateArtwork()
    {
        _artworkCacheKey = null;
        _cachedArtwork = null;
        _cachedTint = null;
    }

    /// <summary>
    /// Libère l'écoute des sessions média. Appelée par le cycle de vie de la
    /// fonctionnalité : une fonctionnalité média désactivée ne doit conserver
    /// aucun abonnement aux sessions système.
    /// </summary>
    public void Shutdown()
    {
        if (_sessionManager is not null)
        {
            try
            {
                _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
            }
            catch
            {
                // Tolérance : la session a pu disparaître entre-temps.
            }
        }

        DetachCurrentSession();
        _sessionManager = null;

        // Une fonctionnalité réactivée ne doit pas réutiliser la pochette de la
        // piste précédemment suivie.
        InvalidateArtwork();
    }

    /// <summary>Vrai tant que les sessions média sont écoutées.</summary>
    public bool IsListening => _sessionManager is not null;

    private void DetachCurrentSession()
    {
        if (_currentSession is null)
        {
            return;
        }

        try
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged -= OnPlaybackInfoChanged;
            _currentSession.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
        }
        catch
        {
            // Tolérance.
        }

        _currentSession = null;
    }

    public async Task<bool> TogglePlayPauseAsync()
    {
        if (_currentSession == null) return false;
        return await _currentSession.TryTogglePlayPauseAsync();
    }

    public async Task<bool> SkipNextAsync()
    {
        if (_currentSession == null) return false;
        return await _currentSession.TrySkipNextAsync();
    }

    public async Task<bool> SkipPreviousAsync()
    {
        if (_currentSession == null) return false;
        return await _currentSession.TrySkipPreviousAsync();
    }

    /// <summary>
    /// Déplace la tête de lecture. La position demandée est relative au début de
    /// la piste, en secondes, conformément au contrat neutre des actions :
    /// l'interface n'a pas à connaître le type <see cref="TimeSpan"/>.
    /// </summary>
    public async Task<bool> SeekAsync(double positionSeconds)
    {
        if (_currentSession == null)
        {
            return false;
        }

        if (double.IsNaN(positionSeconds) || double.IsInfinity(positionSeconds) || positionSeconds < 0)
        {
            return false;
        }

        return await _currentSession.TryChangePlaybackPositionAsync(
            (long)(positionSeconds * TimeSpan.TicksPerSecond));
    }
}
