using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SpaceNotch.Core.Media;

/// <summary>Un morceau de la file d'attente de Spotify.</summary>
public sealed record QueuedTrack(string Title, string Artist);

/// <summary>
/// Spotify (T4) : « J'aime » et la file d'attente, par l'API Web, avec
/// l'application que l'utilisateur déclare chez Spotify (son identifiant
/// client) et une connexion PKCE — aucun secret n'est gardé dans SpaceNotch.
/// Ici, les adresses, les corps et la lecture des réponses ; le réseau est ailleurs.
/// </summary>
public static class SpotifyApi
{
    public const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";

    public const string TokenEndpoint = "https://accounts.spotify.com/api/token";

    public const string ApiBase = "https://api.spotify.com/v1";

    /// <summary>L'adresse de retour à déclarer dans l'application Spotify.</summary>
    public const string RedirectUri = "http://127.0.0.1:43117/spotify/callback";

    /// <summary>Lire la bibliothèque et la lecture, ajouter aux titres likés.</summary>
    public const string Scopes = "user-library-read user-library-modify user-read-playback-state";

    /// <summary>Un vérificateur PKCE : 64 caractères tirés au hasard.</summary>
    public static string CreateVerifier() => Base64Url(RandomNumberGenerator.GetBytes(48));

    /// <summary>Le défi S256 d'un vérificateur.</summary>
    public static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    public static Uri AuthorizeUrl(string clientId, string challenge, string state)
        => new(AuthorizeEndpoint
            + "?client_id=" + Uri.EscapeDataString(clientId)
            + "&response_type=code"
            + "&redirect_uri=" + Uri.EscapeDataString(RedirectUri)
            + "&code_challenge_method=S256"
            + "&code_challenge=" + Uri.EscapeDataString(challenge)
            + "&state=" + Uri.EscapeDataString(state)
            + "&scope=" + Uri.EscapeDataString(Scopes));

    public static IReadOnlyList<KeyValuePair<string, string>> TokenRequest(string clientId, string code, string verifier) =>
    [
        new("client_id", clientId),
        new("grant_type", "authorization_code"),
        new("code", code),
        new("redirect_uri", RedirectUri),
        new("code_verifier", verifier)
    ];

    public static IReadOnlyList<KeyValuePair<string, string>> RefreshRequest(string clientId, string refreshToken) =>
    [
        new("client_id", clientId),
        new("grant_type", "refresh_token"),
        new("refresh_token", refreshToken)
    ];

    /// <summary>Le code et l'état d'un retour sur l'adresse locale (<c>?code=…&amp;state=…</c>).</summary>
    public static (string? Code, string? State, string? Error) ReadCallback(string? query)
    {
        string? code = null, state = null, error = null;

        foreach (string pair in (query ?? string.Empty).TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] kv = pair.Split('=', 2);
            string value = kv.Length == 2 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;

            switch (kv[0])
            {
                case "code": code = value; break;
                case "state": state = value; break;
                case "error": error = value; break;
            }
        }

        return (code, state, error);
    }

    /// <summary>La recherche du morceau en cours : le lecteur Windows ne donne pas l'identifiant Spotify.</summary>
    public static Uri SearchUrl(string artist, string title)
        => new(ApiBase + "/search?type=track&limit=1&q=" + Uri.EscapeDataString("track:" + Lyrics.CleanTitle(title) + " artist:" + artist));

    public static Uri SavedUrl(string trackId) => new(ApiBase + "/me/tracks?ids=" + Uri.EscapeDataString(trackId));

    public static Uri ContainsUrl(string trackId) => new(ApiBase + "/me/tracks/contains?ids=" + Uri.EscapeDataString(trackId));

    public static readonly Uri QueueUrl = new(ApiBase + "/me/player/queue");

    /// <summary>L'identifiant du premier résultat d'une recherche.</summary>
    public static string? ReadFirstTrackId(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("tracks", out JsonElement tracks)
                && tracks.TryGetProperty("items", out JsonElement items)
                && items.ValueKind == JsonValueKind.Array
                && items.GetArrayLength() > 0
                && items[0].TryGetProperty("id", out JsonElement id)
                && id.ValueKind == JsonValueKind.String)
            {
                string value = id.GetString()!;
                return value.Length is > 0 and <= 40 && value.All(char.IsAsciiLetterOrDigit) ? value : null;
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    /// <summary>La réponse de <c>contains</c> : <c>[true]</c>.</summary>
    public static bool? ReadContains(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            return doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0
                ? doc.RootElement[0].ValueKind == JsonValueKind.True
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Les prochains morceaux de la file, trois au plus.</summary>
    public static IReadOnlyList<QueuedTrack> ReadQueue(string json, int max = 3)
    {
        var tracks = new List<QueuedTrack>();

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("queue", out JsonElement queue) || queue.ValueKind != JsonValueKind.Array)
            {
                return tracks;
            }

            foreach (JsonElement item in queue.EnumerateArray())
            {
                if (tracks.Count >= max)
                {
                    break;
                }

                string? name = item.TryGetProperty("name", out JsonElement n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
                string? artist = item.TryGetProperty("artists", out JsonElement a) && a.ValueKind == JsonValueKind.Array && a.GetArrayLength() > 0
                    && a[0].TryGetProperty("name", out JsonElement an) && an.ValueKind == JsonValueKind.String
                        ? an.GetString()
                        : item.TryGetProperty("show", out JsonElement show) && show.TryGetProperty("name", out JsonElement sn) ? sn.GetString() : null;

                if (!string.IsNullOrWhiteSpace(name))
                {
                    tracks.Add(new QueuedTrack(name!, artist ?? string.Empty));
                }
            }
        }
        catch (JsonException)
        {
        }

        return tracks;
    }

    /// <summary>« Ensuite : Titre — Artiste ».</summary>
    public static string? NextLine(IReadOnlyList<QueuedTrack> queue, bool french)
        => queue.Count == 0 ? null
            : (french ? "Ensuite : " : "Up next: ") + queue[0].Title + (queue[0].Artist.Length > 0 ? " — " + queue[0].Artist : string.Empty);

    /// <summary>Un identifiant client Spotify : 32 caractères hexadécimaux.</summary>
    public static bool IsClientId(string? value)
        => value is { Length: 32 } && value.All(char.IsAsciiHexDigit);

    /// <summary>Vrai si l'application en cours de lecture est Spotify.</summary>
    public static bool IsSpotify(string? appId)
        => appId is not null && appId.Contains("Spotify", StringComparison.OrdinalIgnoreCase);

    private static string Base64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Le jeton d'une réponse OAuth2.</summary>
    public static (string Access, string? Refresh, int ExpiresIn)? ReadToken(string json)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            if (!root.TryGetProperty("access_token", out JsonElement access) || access.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            string? refresh = root.TryGetProperty("refresh_token", out JsonElement r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            int expires = root.TryGetProperty("expires_in", out JsonElement e) && e.ValueKind == JsonValueKind.Number ? e.GetInt32() : 3600;
            return (access.GetString()!, refresh, expires);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
