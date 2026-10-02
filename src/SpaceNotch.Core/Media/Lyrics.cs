using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SpaceNotch.Core.Media;

/// <summary>Une ligne de paroles et l'instant où elle commence.</summary>
public sealed record LyricLine(TimeSpan At, string Text);

/// <summary>Des paroles synchronisées, triées par instant.</summary>
public sealed class SyncedLyrics
{
    public SyncedLyrics(IReadOnlyList<LyricLine> lines) => Lines = lines;

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>La ligne chantée à cet instant, ou -1 avant la première.</summary>
    public int IndexAt(TimeSpan position)
    {
        int low = 0;
        int high = Lines.Count - 1;
        int found = -1;

        while (low <= high)
        {
            int mid = (low + high) / 2;

            if (Lines[mid].At <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }

    /// <summary>La ligne chantée, ou <c>null</c> (avant la première, ou sur une ligne vide).</summary>
    public string? LineAt(TimeSpan position)
        => IndexAt(position) is >= 0 and int i && Lines[i].Text.Length > 0 ? Lines[i].Text : null;

    /// <summary>La prochaine ligne non vide après celle chantée, ou <c>null</c> à la fin.</summary>
    public string? NextLineAt(TimeSpan position)
    {
        for (int i = IndexAt(position) + 1; i < Lines.Count; i++)
        {
            if (Lines[i].Text.Length > 0)
            {
                return Lines[i].Text;
            }
        }

        return null;
    }
}

/// <summary>
/// Paroles synchronisées (T4) : le format LRC (<c>[01:23.45]une ligne</c>) et
/// LRCLIB, une base libre et sans clé. La requête ne part que si l'utilisateur
/// a allumé les paroles ; elle ne porte que l'artiste, le titre, l'album et la durée.
/// </summary>
public static partial class Lyrics
{
    public const string LrcLibBase = "https://lrclib.net/api/get";

    /// <summary>Lit un texte LRC. Rend <c>null</c> s'il n'y a aucune ligne datée.</summary>
    public static SyncedLyrics? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        var lines = new List<LyricLine>();
        TimeSpan offset = TimeSpan.Zero;

        foreach (string raw in lrc.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            Match off = OffsetPattern().Match(line);

            if (off.Success && int.TryParse(off.Groups["ms"].Value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ms))
            {
                // Un décalage positif avance les paroles.
                offset = TimeSpan.FromMilliseconds(-ms);
                continue;
            }

            MatchCollection stamps = StampPattern().Matches(line);

            if (stamps.Count == 0)
            {
                continue;
            }

            // Plusieurs horodatages pour un refrain : « [00:12.00][01:30.00]la la ».
            Match last = stamps[^1];
            string text = line[(last.Index + last.Length)..].Trim();

            foreach (Match stamp in stamps)
            {
                int minutes = int.Parse(stamp.Groups["m"].Value, CultureInfo.InvariantCulture);
                int seconds = int.Parse(stamp.Groups["s"].Value, CultureInfo.InvariantCulture);
                string fraction = stamp.Groups["f"].Value;
                double fractionSeconds = fraction.Length == 0 ? 0 : double.Parse("0." + fraction, CultureInfo.InvariantCulture);

                if (seconds > 59)
                {
                    continue;
                }

                TimeSpan at = TimeSpan.FromSeconds((minutes * 60) + seconds + fractionSeconds) + offset;
                lines.Add(new LyricLine(at < TimeSpan.Zero ? TimeSpan.Zero : at, text));
            }
        }

        return lines.Count == 0 ? null : new SyncedLyrics([.. lines.OrderBy(l => l.At)]);
    }

    /// <summary>L'adresse de la requête LRCLIB ; la durée aide à choisir la bonne version.</summary>
    public static Uri? LrcLibQuery(string? artist, string? title, string? album, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        string query = "artist_name=" + Uri.EscapeDataString(artist.Trim())
            + "&track_name=" + Uri.EscapeDataString(CleanTitle(title))
            + (string.IsNullOrWhiteSpace(album) ? string.Empty : "&album_name=" + Uri.EscapeDataString(album.Trim()))
            + (duration > TimeSpan.Zero ? "&duration=" + ((int)Math.Round(duration.TotalSeconds)).ToString(CultureInfo.InvariantCulture) : string.Empty);

        return new Uri(LrcLibBase + "?" + query);
    }

    /// <summary>Les paroles synchronisées d'une réponse LRCLIB, ou <c>null</c> (instrumental, absentes).</summary>
    public static SyncedLyrics? FromLrcLib(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using JsonDocument doc = JsonDocument.Parse(json);

            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("syncedLyrics", out JsonElement synced)
                && synced.ValueKind == JsonValueKind.String
                    ? Parse(synced.GetString())
                    : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>« Titre - Remastered 2011 », « Titre (feat. X) » : le titre seul, pour la recherche.</summary>
    public static string CleanTitle(string title)
    {
        string clean = DecorationPattern().Replace(title, string.Empty).Trim();
        return clean.Length == 0 ? title.Trim() : clean;
    }

    [GeneratedRegex(@"\[(?<m>\d{1,3}):(?<s>\d{2})(?:[.:](?<f>\d{1,3}))?\]", RegexOptions.CultureInvariant)]
    private static partial Regex StampPattern();

    [GeneratedRegex(@"^\s*\[offset:\s*(?<ms>[+-]?\d+)\s*\]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex OffsetPattern();

    [GeneratedRegex(@"\s*(?:\((?:feat\.?|ft\.?|with)[^)]*\)|\[(?:feat\.?|ft\.?)[^\]]*\]|\s-\s(?:\d{4}\s)?(?:remaster(?:ed)?|live|radio edit|version)[^-]*$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DecorationPattern();
}
