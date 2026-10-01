using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Media;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch_App.Assistant;

namespace SpaceNotch_App.Media;

/// <summary>
/// Spotify (T4) : « J'aime » et la file d'attente, par l'API Web. Connexion
/// PKCE avec l'identifiant client de l'application que l'utilisateur déclare
/// chez Spotify ; le jeton est gardé dans le coffre de Windows (ADR-026).
/// </summary>
internal sealed class SpotifyClient
{
    private const string TokenResource = "SpaceNotch.Spotify.Token";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly Func<string?> _clientId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _access;
    private DateTimeOffset _expires;
    private string? _lastKey;
    private string? _lastId;

    public SpotifyClient(Func<string?> clientId) => _clientId = clientId ?? throw new ArgumentNullException(nameof(clientId));

    /// <summary>Un jeton est gardé : l'utilisateur s'est connecté.</summary>
    public static bool IsConnected => SecretVault.Read(TokenResource) is not null;

    public static void Disconnect() => SecretVault.Remove(TokenResource);

    /// <summary>
    /// « Connecter » : ouvre la page d'accord de Spotify dans le navigateur et
    /// attend le retour sur l'adresse locale, deux minutes au plus.
    /// </summary>
    public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
    {
        if (_clientId() is not { } clientId || !SpotifyApi.IsClientId(clientId))
        {
            return false;
        }

        string verifier = SpotifyApi.CreateVerifier();
        string state = SpotifyApi.CreateVerifier()[..16];
        using var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:43117/spotify/");

        try
        {
            listener.Start();
        }
        catch (HttpListenerException ex)
        {
            MiniLogger.Log("[SPOTIFY] Port 43117 indisponible", ex);
            return false;
        }

        await global::Windows.System.Launcher.LaunchUriAsync(SpotifyApi.AuthorizeUrl(clientId, SpotifyApi.Challenge(verifier), state));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(timeout.Token).ConfigureAwait(false);
            (string? code, string? returned, string? error) = SpotifyApi.ReadCallback(context.Request.Url?.Query);
            bool ok = error is null && code is not null && returned == state;

            byte[] page = Encoding.UTF8.GetBytes(ok
                ? "<html><body style='font-family:sans-serif;background:#000;color:#fff'><p>SpaceNotch est connecté à Spotify. Tu peux fermer cet onglet.</p></body></html>"
                : "<html><body style='font-family:sans-serif;background:#000;color:#fff'><p>Connexion refusée.</p></body></html>");
            context.Response.ContentType = "text/html; charset=utf-8";
            await context.Response.OutputStream.WriteAsync(page, timeout.Token).ConfigureAwait(false);
            context.Response.Close();

            return ok && await ExchangeAsync(SpotifyApi.TokenRequest(clientId, code!, verifier), timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Le morceau est-il dans les titres likés ? <c>null</c> : inconnu (non connecté, introuvable).</summary>
    public async Task<bool?> IsLikedAsync(string artist, string title, CancellationToken cancellationToken)
    {
        if (await TrackIdAsync(artist, title, cancellationToken).ConfigureAwait(false) is not { } id)
        {
            return null;
        }

        string? body = await GetAsync(SpotifyApi.ContainsUrl(id), cancellationToken).ConfigureAwait(false);
        return body is null ? null : SpotifyApi.ReadContains(body);
    }

    /// <summary>Ajoute (ou retire) le morceau des titres likés.</summary>
    public async Task<bool> SetLikedAsync(string artist, string title, bool liked, CancellationToken cancellationToken)
    {
        if (await TrackIdAsync(artist, title, cancellationToken).ConfigureAwait(false) is not { } id
            || await TokenAsync(cancellationToken).ConfigureAwait(false) is not { } token)
        {
            return false;
        }

        using var request = new HttpRequestMessage(liked ? HttpMethod.Put : HttpMethod.Delete, SpotifyApi.SavedUrl(id));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }

    /// <summary>Les prochains morceaux de la file.</summary>
    public async Task<IReadOnlyList<QueuedTrack>> QueueAsync(CancellationToken cancellationToken)
    {
        string? body = await GetAsync(SpotifyApi.QueueUrl, cancellationToken).ConfigureAwait(false);
        return body is null ? [] : SpotifyApi.ReadQueue(body);
    }

    private async Task<string?> TrackIdAsync(string artist, string title, CancellationToken cancellationToken)
    {
        string key = artist + "\u001f" + title;

        if (key == _lastKey)
        {
            return _lastId;
        }

        string? body = await GetAsync(SpotifyApi.SearchUrl(artist, title), cancellationToken).ConfigureAwait(false);
        _lastKey = key;
        _lastId = body is null ? null : SpotifyApi.ReadFirstTrackId(body);
        return _lastId;
    }

    private async Task<string?> GetAsync(Uri url, CancellationToken cancellationToken)
    {
        if (await TokenAsync(cancellationToken).ConfigureAwait(false) is not { } token)
        {
            return null;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        try
        {
            using HttpResponseMessage response = await Http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }

    /// <summary>Un jeton valide, renouvelé si besoin.</summary>
    private async Task<string?> TokenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            if (_access is not null && DateTimeOffset.UtcNow < _expires)
            {
                return _access;
            }

            if (_clientId() is not { } clientId
                || SecretVault.Read(TokenResource) is not { } stored
                || SpotifyApi.ReadToken(stored) is not { Refresh: { } refresh })
            {
                return null;
            }

            return await ExchangeAsync(SpotifyApi.RefreshRequest(clientId, refresh), cancellationToken).ConfigureAwait(false) ? _access : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<bool> ExchangeAsync(IReadOnlyList<KeyValuePair<string, string>> form, CancellationToken cancellationToken)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using HttpResponseMessage response = await Http.PostAsync(new Uri(SpotifyApi.TokenEndpoint), content, cancellationToken).ConfigureAwait(false);
            string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode || SpotifyApi.ReadToken(body) is not { } token)
            {
                MiniLogger.Log($"[SPOTIFY] Jeton refusé ({(int)response.StatusCode}).");
                return false;
            }

            // Un renouvellement peut ne pas rendre de nouveau jeton de renouvellement : on garde l'ancien.
            string? refresh = token.Refresh ?? (SecretVault.Read(TokenResource) is { } old ? SpotifyApi.ReadToken(old)?.Refresh : null);
            if (refresh is not null)
            {
                // Seul le jeton de renouvellement est gardé ; le jeton d'accès vit en mémoire.
                SecretVault.Save(TokenResource, new System.Text.Json.Nodes.JsonObject { ["access_token"] = string.Empty, ["refresh_token"] = refresh }.ToJsonString());
            }

            _access = token.Access;
            _expires = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, token.ExpiresIn - 60));
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return false;
        }
    }
}
