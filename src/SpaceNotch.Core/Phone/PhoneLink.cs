using System.Globalization;

namespace SpaceNotch.Core.Phone;

/// <summary>Où en est un appel relayé par Lien avec Windows.</summary>
public enum CallState
{
    /// <summary>Le téléphone sonne.</summary>
    Ringing,

    /// <summary>L'appel est en cours.</summary>
    Active,

    /// <summary>Personne n'a répondu.</summary>
    Missed,

    /// <summary>L'appel est fini.</summary>
    Ended
}

/// <summary>Un appel lu dans une notification : qui, et à quel moment de l'appel.</summary>
public sealed record PhoneCall(string Caller, CallState State);

/// <summary>
/// Lien avec Windows (T1, T2) : le téléphone relaie ses appels et ses
/// notifications au PC. SpaceNotch ne parle pas au téléphone ; il lit ce que
/// Windows affiche déjà, dans les deux langues de l'application.
/// </summary>
public static class PhoneLink
{
    /// <summary>L'adresse qui ouvre Lien avec Windows sur les appels.</summary>
    public const string CallingUri = "ms-phone:calling";

    private static readonly string[] AppNames =
    [
        "Phone Link", "Lien avec Windows", "Mobile connecté", "Your Phone", "Votre téléphone", "Lien Mobile", "Mobile Connected"
    ];

    private static readonly (string Word, CallState State)[] CallWords =
    [
        ("appel entrant", CallState.Ringing),
        ("incoming call", CallState.Ringing),
        ("appel vidéo entrant", CallState.Ringing),
        ("appel manqué", CallState.Missed),
        ("missed call", CallState.Missed),
        ("appel en cours", CallState.Active),
        ("ongoing call", CallState.Active),
        ("call in progress", CallState.Active),
        ("appel terminé", CallState.Ended),
        ("call ended", CallState.Ended)
    ];

    /// <summary>Vrai si la notification vient de Lien avec Windows.</summary>
    public static bool IsPhoneLink(string? appName)
        => appName is not null && AppNames.Any(n => appName.Trim().Equals(n, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Un appel, si la notification en décrit un. Le mot-clé peut être dans le
    /// titre (l'appelant est alors le texte) ou dans le texte (l'appelant est le titre).
    /// </summary>
    public static PhoneCall? ReadCall(string? appName, string? title, string? body)
    {
        if (!IsPhoneLink(appName))
        {
            return null;
        }

        string t = (title ?? string.Empty).Trim();
        string b = (body ?? string.Empty).Trim();

        foreach ((string word, CallState state) in CallWords)
        {
            if (t.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return new PhoneCall(Caller(b, t, word), state);
            }

            if (b.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return new PhoneCall(Caller(t, b, word), state);
            }
        }

        return null;
    }

    /// <summary>« 0:42 », « 12:05 », « 1:02:03 » : la durée d'un appel.</summary>
    public static string Duration(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return elapsed.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{(int)elapsed.TotalMinutes}:{elapsed.Seconds:00}");
    }

    private static string Caller(string preferred, string withKeyword, string keyword)
    {
        if (preferred.Length > 0)
        {
            return Short(preferred);
        }

        // « Appel entrant de Maman » : ce qui suit le mot-clé.
        int at = withKeyword.IndexOf(keyword, StringComparison.OrdinalIgnoreCase);
        string rest = withKeyword[(at + keyword.Length)..].Trim(' ', ':', '·', '-', '–');

        foreach (string prefix in new[] { "de ", "from " })
        {
            if (rest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                rest = rest[prefix.Length..];
            }
        }

        return rest.Length > 0 ? Short(rest) : "?";
    }

    private static string Short(string text)
    {
        string line = text.Split('\n', 2)[0].Trim();
        return line.Length <= 40 ? line : string.Concat(line.AsSpan(0, 39), "…");
    }
}
