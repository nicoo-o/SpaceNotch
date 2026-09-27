using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Nom lisible de l'application qui joue : Windows donne un identifiant
/// technique (« Spotify.exe », « SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify »,
/// « MSEdge »), la notch affiche « Spotify », « Edge ».
/// </summary>
public static class MediaSource
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["spotify"] = "Spotify",
        ["msedge"] = "Edge",
        ["chrome"] = "Chrome",
        ["firefox"] = "Firefox",
        ["brave"] = "Brave",
        ["opera"] = "Opera",
        ["vlc"] = "VLC",
        ["applemusic"] = "Apple Music",
        ["itunes"] = "iTunes",
        ["zunemusic"] = "Media Player",
        ["microsoft.media.player"] = "Media Player",
        ["deezer"] = "Deezer",
        ["tidal"] = "TIDAL",
        ["youtubemusic"] = "YouTube Music",
    };

    /// <summary>Nom affichable, ou chaîne vide si l'identifiant n'apprend rien.</summary>
    public static string FriendlyName(string? appId)
    {
        if (string.IsNullOrWhiteSpace(appId))
        {
            return string.Empty;
        }

        string id = appId.Trim();

        // Identifiant de paquet : « Éditeur.Produit_hash!Application ».
        int bang = id.IndexOf('!', StringComparison.Ordinal);
        string package = bang >= 0 ? id[..bang] : id;
        string application = bang >= 0 ? id[(bang + 1)..] : string.Empty;

        int underscore = package.IndexOf('_', StringComparison.Ordinal);
        if (underscore > 0)
        {
            package = package[..underscore];
        }

        if (package.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            package = package[..^4];
        }

        foreach (string candidate in new[] { package, application, LastSegment(package) })
        {
            foreach ((string key, string name) in Known)
            {
                if (candidate.Length > 0 && candidate.Contains(key, StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        string last = LastSegment(package);
        return last.Length == 0 ? string.Empty : char.ToUpperInvariant(last[0]) + last[1..];
    }

    private static string LastSegment(string value)
    {
        int dot = value.LastIndexOf('.');
        return dot >= 0 && dot < value.Length - 1 ? value[(dot + 1)..] : value;
    }
}
