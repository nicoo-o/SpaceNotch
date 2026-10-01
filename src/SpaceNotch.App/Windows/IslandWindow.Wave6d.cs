using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Media;
using SpaceNotch.Features.Calendar;
using SpaceNotch.Features.Phone;
using SpaceNotch.Features.Social;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Audio;
using SpaceNotch.Platform.Windows.Discord;
using SpaceNotch.Platform.Windows.Media;
using SpaceNotch_App.Assistant;
using SpaceNotch_App.Media;
using SpaceNotch_App.Phone;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Vague 6d, le téléphone et les salons : l'appel en vert (T1), la livraison
/// et son véhicule (T2), la salle vocale Discord (T3), les paroles et
/// « J'aime » sur Spotify (T4), le miroir avant une réunion (W5). Voir ADR-026.
/// </summary>
public sealed partial class IslandWindow
{
    private PhoneFeature? _phoneFeature;
    private DiscordVoiceFeature? _discordFeature;
    private DiscordIpcClient? _discordClient;
    private readonly LyricsService _lyricsService = new();
    private SpotifyClient? _spotify;
    private CameraMirror? _mirror;
    private CancellationTokenSource? _trackCts;
    private string? _trackKey;
    private MediaTrackInfo? _track;

    private IEnumerable<IIslandFeature> CreateWave6dFeatures()
    {
        _phoneFeature = new PhoneFeature(_activityManager, _eventBus, _settings.ShowPhone);
        _discordFeature = new DiscordVoiceFeature(_activityManager, _eventBus, _settings.ShowDiscord);
        return [_phoneFeature, _discordFeature];
    }

    private void WireWave6d()
    {
        if (_phoneFeature is { } phone)
        {
            _notificationFeature.Intercept = phone.Offer;
            phone.OpenRequested += uri => OnUiThread(() => _ = LaunchAsync(uri));
        }

        // Discord : le client ne tourne que si une application est renseignée.
        _discordClient = new DiscordIpcClient(
            () => DiscordCredentials(),
            () => SecretVault.Read(DiscordLink.TokenResource),
            token =>
            {
                if (token is null)
                {
                    SecretVault.Remove(DiscordLink.TokenResource);
                }
                else
                {
                    SecretVault.Save(DiscordLink.TokenResource, token);
                }
            });
        DiscordLink.Client = _discordClient;
        _discordClient.RoomChanged += (id, name, members, muted) => _discordFeature?.Show(id, name, members, muted);

        if (_discordFeature is not null)
        {
            _discordFeature.SetMute = mute => _discordClient.SetMuteAsync(mute);
        }

        ApplyDiscord();

        // Paroles et Spotify : à chaque morceau, au plus une requête de chaque.
        _spotify = new SpotifyClient(() => _settings.SpotifyClientId is { Length: > 0 } id ? id : null);
        _mediaFeature.SessionManager.TrackChanged += OnTrackForExtras;
        MediaSceneView.LikeRequested += liked => _ = LikeAsync(liked);

        // Miroir : la webcam au survol de « Rejoindre ».
        InfoSceneView.ActionHovered += OnActionHovered;
    }

    private (string ClientId, string Secret)? DiscordCredentials()
        => _settings.DiscordClientId is { Length: > 0 } id && SecretVault.Read(DiscordLink.SecretResource) is { } secret ? (id, secret) : null;

    /// <summary>Démarre ou arrête le client Discord selon les réglages.</summary>
    private void ApplyDiscord()
    {
        if (_discordClient is null)
        {
            return;
        }

        if (_settings.ShowDiscord && DiscordCredentials() is not null)
        {
            _discordClient.Start();
        }
        else
        {
            _discordClient.Stop();
            _discordFeature?.Show(null, null, [], false);
        }
    }

    private static async Task LaunchAsync(string uri)
    {
        try
        {
            await global::Windows.System.Launcher.LaunchUriAsync(new Uri(uri));
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[TÉLÉPHONE] Lien avec Windows introuvable", ex);
        }
    }

    // ---- T4 : paroles et Spotify ------------------------------------------

    private void OnTrackForExtras(object? sender, MediaTrackInfo? track)
    {
        _track = track;
        string? key = track is null ? null : track.Artist + "\u001f" + track.Title;

        if (key == _trackKey)
        {
            return;
        }

        _trackKey = key;
        _trackCts?.Cancel();
        _trackCts?.Dispose();
        _trackCts = null;

        OnUiThread(() =>
        {
            MediaSceneView.SetLyrics(null);
            MediaSceneView.SetNext(null);
            MediaSceneView.SetLiked(null);
        });

        if (track is null || string.IsNullOrWhiteSpace(track.Title) || string.IsNullOrWhiteSpace(track.Artist))
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _trackCts = cts;
        _ = LoadExtrasAsync(track, cts.Token);
    }

    private async Task LoadExtrasAsync(MediaTrackInfo track, CancellationToken cancellationToken)
    {
        try
        {
            // Un morceau qu'on saute en une seconde ne mérite pas de requête.
            await Task.Delay(800, cancellationToken).ConfigureAwait(false);

            if (_settings.ShowLyrics)
            {
                SyncedLyrics? lyrics = await _lyricsService.FindAsync(track.Artist, track.Title, track.AlbumTitle, track.Duration, cancellationToken).ConfigureAwait(false);
                OnUiThread(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        MediaSceneView.SetLyrics(lyrics);
                    }
                });
            }

            if (_spotify is { } spotify && SpotifyApi.IsSpotify(track.AppId) && SpotifyClient.IsConnected)
            {
                bool? liked = await spotify.IsLikedAsync(track.Artist, track.Title, cancellationToken).ConfigureAwait(false);
                IReadOnlyList<QueuedTrack> queue = await spotify.QueueAsync(cancellationToken).ConfigureAwait(false);
                OnUiThread(() =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        MediaSceneView.SetLiked(liked);
                        MediaSceneView.SetNext(SpotifyApi.NextLine(queue, Lang.French));
                    }
                });
            }
        }
        catch (OperationCanceledException)
        {
            // Morceau suivant.
        }
    }

    private async Task LikeAsync(bool liked)
    {
        if (_spotify is not { } spotify || _track is not { } track)
        {
            return;
        }

        bool ok = await spotify.SetLikedAsync(track.Artist, track.Title, liked, CancellationToken.None).ConfigureAwait(false);

        if (!ok)
        {
            OnUiThread(() => MediaSceneView.SetLiked(!liked));
        }
    }

    // ---- W5 : le miroir avant une réunion ----------------------------------

    private void OnActionHovered(string activityId, string actionId, bool entered)
    {
        if (actionId != MeetingFeature.JoinAction || !_settings.CameraMirror)
        {
            return;
        }

        _mirror ??= new CameraMirror();

        if (!entered)
        {
            InfoSceneView.ShowMirror(false, null);
            _ = _mirror.StopAsync(InfoSceneView.Mirror);
            return;
        }

        InfoSceneView.ShowMirror(true, MicrophoneLine());
        _ = StartMirrorAsync();
    }

    private async Task StartMirrorAsync()
    {
        if (_mirror is null)
        {
            return;
        }

        bool camera = await _mirror.StartAsync(InfoSceneView.Mirror);

        if (!camera)
        {
            InfoSceneView.ShowMirror(true, Lang.T("Caméra indisponible", "Camera unavailable") + " · " + MicrophoneLine());
        }
    }

    private static string MicrophoneLine() => MicrophoneState.IsMuted() switch
    {
        true => Lang.T("Micro coupé", "Mic muted"),
        false => Lang.T("Micro ouvert", "Mic on"),
        null => Lang.T("Pas de micro", "No microphone")
    };

    /// <summary>La scène générique se repose : la caméra est rendue.</summary>
    private void RestMirror()
    {
        if (_mirror is not null)
        {
            _ = _mirror.StopAsync(InfoSceneView.Mirror);
        }
    }
}
