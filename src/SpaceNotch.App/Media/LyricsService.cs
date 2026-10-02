using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Media;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Media;

/// <summary>
/// Paroles synchronisées depuis LRCLIB (T4). Une requête par morceau, gardée en
/// mémoire : revenir sur un titre ne redemande rien. N'est appelé que si les
/// paroles sont allumées dans les réglages (ADR-026).
/// </summary>
internal sealed class LyricsService
{
    private static readonly HttpClient Http = CreateClient();

    private readonly Dictionary<string, SyncedLyrics?> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _order = new();

    public async Task<SyncedLyrics?> FindAsync(string artist, string title, string? album, TimeSpan duration, CancellationToken cancellationToken)
    {
        string key = artist + "\u001f" + title;

        lock (_cache)
        {
            if (_cache.TryGetValue(key, out SyncedLyrics? cached))
            {
                return cached;
            }
        }

        if (Lyrics.LrcLibQuery(artist, title, album, duration) is not { } url)
        {
            return null;
        }

        SyncedLyrics? lyrics = null;

        try
        {
            using HttpResponseMessage response = await Http.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                lyrics = Lyrics.FromLrcLib(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            }
            else if (response.StatusCode != HttpStatusCode.NotFound)
            {
                // Une erreur passagère n'est pas mise en mémoire : on redemandera.
                return null;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                MiniLogger.Log("[PAROLES] LRCLIB injoignable", ex);
            }

            return null;
        }

        lock (_cache)
        {
            _cache[key] = lyrics;
            _order.Enqueue(key);

            while (_order.Count > 30)
            {
                _cache.Remove(_order.Dequeue());
            }
        }

        return lyrics;
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        // LRCLIB demande qu'on se présente.
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SpaceNotch (https://github.com/nicoo-o/SpaceNotch)");
        return client;
    }
}
