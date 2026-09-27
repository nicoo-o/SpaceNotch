using System.Text.RegularExpressions;

namespace SpaceNotch.Core.Calendar;

/// <summary>
/// Prochain rendez-vous (F2) : le lien de visioconférence d'une invitation —
/// Teams, Meet, Zoom ou Webex —, cherché dans le lien déclaré, le lieu puis le
/// corps. Le premier lien reconnu gagne ; un lien quelconque n'est jamais
/// proposé comme « Rejoindre ».
/// </summary>
public static partial class MeetingLink
{
    public static Uri? Find(params string?[] sources)
    {
        foreach (string? text in sources)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            foreach (Match match in UrlPattern().Matches(text))
            {
                string candidate = match.Value.TrimEnd('.', ',', ')', '>', '"', '\'');

                if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) && IsMeeting(uri))
                {
                    return uri;
                }
            }
        }

        return null;
    }

    /// <summary>Nom du service, pour le bouton (« Teams », « Meet »…).</summary>
    public static string ServiceOf(Uri link)
    {
        ArgumentNullException.ThrowIfNull(link);
        string host = link.Host.ToLowerInvariant();

        return host switch
        {
            _ when host.EndsWith("teams.microsoft.com", StringComparison.Ordinal) || host.EndsWith("teams.live.com", StringComparison.Ordinal) => "Teams",
            _ when host == "meet.google.com" => "Meet",
            _ when host.EndsWith("zoom.us", StringComparison.Ordinal) => "Zoom",
            _ when host.EndsWith("webex.com", StringComparison.Ordinal) => "Webex",
            _ => host
        };
    }

    private static bool IsMeeting(Uri uri)
    {
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        string path = uri.AbsolutePath.ToLowerInvariant();

        return (host.EndsWith("teams.microsoft.com", StringComparison.Ordinal) && path.Contains("meetup-join", StringComparison.Ordinal))
            || (host.EndsWith("teams.live.com", StringComparison.Ordinal) && path.StartsWith("/meet", StringComparison.Ordinal))
            || (host == "meet.google.com" && path.Length > 4)
            || (host.EndsWith("zoom.us", StringComparison.Ordinal) && (path.StartsWith("/j/", StringComparison.Ordinal) || path.StartsWith("/my/", StringComparison.Ordinal)))
            || (host.EndsWith("webex.com", StringComparison.Ordinal) && (path.Contains("/meet", StringComparison.Ordinal) || path.Contains("/j.php", StringComparison.Ordinal)));
    }

    [GeneratedRegex(@"https://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex UrlPattern();
}
