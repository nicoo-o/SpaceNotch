using System.Globalization;
using System.Text;

namespace SpaceNotch.Core.Launcher;

/// <summary>
/// Classement de la recherche, façon Spotlight et Raycast.
///
/// <para>
/// Un nom qui <em>commence</em> par la requête passe devant un nom dont un
/// <em>mot</em> commence par elle, qui passe devant les initiales (« vsc » →
/// « Visual Studio Code »), qui passent devant une simple sous-chaîne. Les
/// accents et la casse ne comptent pas : « ecran » trouve « Écran ». Un bonus
/// d'usage — favori, fréquence — départage à pertinence égale, sans jamais
/// faire remonter un résultat qui ne correspond pas.
/// </para>
/// </summary>
public static class LauncherRanking
{
    public const int Prefix = 100;
    public const int WordStart = 80;
    public const int Initials = 60;
    public const int Substring = 40;

    /// <summary>
    /// Pertinence de <paramref name="name"/> pour <paramref name="query"/>, 0 si
    /// le nom ne correspond pas. Les passages trouvés sont renvoyés dans le nom
    /// d'origine, pour être mis en valeur.
    /// </summary>
    public static int Score(string name, string query, out IReadOnlyList<TextMatch> matches)
    {
        matches = [];

        if (string.IsNullOrEmpty(name) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        string n = Fold(name);
        string q = Fold(query.Trim());

        if (n.StartsWith(q, StringComparison.Ordinal))
        {
            matches = [new TextMatch(0, q.Length)];
            return Prefix + Tightness(n, q);
        }

        for (int i = 1; i < n.Length; i++)
        {
            if (IsWordStart(n, i) && string.CompareOrdinal(n, i, q, 0, q.Length) == 0)
            {
                matches = [new TextMatch(i, q.Length)];
                return WordStart + Tightness(n, q);
            }
        }

        if (q.Length >= 2 && !q.Contains(' ', StringComparison.Ordinal) && MatchInitials(n, q) is { } initials)
        {
            matches = initials;
            return Initials;
        }

        int index = n.IndexOf(q, StringComparison.Ordinal);

        if (index >= 0)
        {
            matches = [new TextMatch(index, q.Length)];
            return Substring + Tightness(n, q);
        }

        return 0;
    }

    /// <summary>Bonus d'usage : un favori, puis la fréquence de lancement, plafonnée.</summary>
    public static int UsageBonus(bool favorite, int launches)
        => (favorite ? 12 : 0) + Math.Min(10, Math.Max(0, launches));

    /// <summary>
    /// Forme de comparaison : minuscules, sans accents. La longueur est
    /// conservée caractère pour caractère, pour que les positions trouvées
    /// valent dans le nom d'origine.
    /// </summary>
    public static string Fold(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var builder = new StringBuilder(text.Length);

        foreach (char c in text)
        {
            string decomposed = c.ToString().Normalize(NormalizationForm.FormD);
            char basic = decomposed[0];

            if (CharUnicodeInfo.GetUnicodeCategory(basic) == UnicodeCategory.NonSpacingMark)
            {
                basic = c;
            }

            builder.Append(char.ToLowerInvariant(basic));
        }

        return builder.ToString();
    }

    /// <summary>Un nom court qui correspond gagne sur un nom long : « Paint » avant « Paint 3D Studio ».</summary>
    private static int Tightness(string name, string query)
        => Math.Clamp(10 - ((name.Length - query.Length) / 4), 0, 10);

    private static bool IsWordStart(string text, int index)
    {
        char previous = text[index - 1];
        char current = text[index];

        return !char.IsLetterOrDigit(previous) && char.IsLetterOrDigit(current)
            || (char.IsLower(previous) && char.IsUpper(current))
            || (char.IsLetter(previous) && char.IsDigit(current));
    }

    /// <summary>« vsc » sur « visual studio code » : chaque lettre ouvre un mot, dans l'ordre.</summary>
    private static List<TextMatch>? MatchInitials(string name, string query)
    {
        var found = new List<TextMatch>(query.Length);
        int q = 0;

        for (int i = 0; i < name.Length && q < query.Length; i++)
        {
            bool start = i == 0 || IsWordStart(name, i);

            if (start && name[i] == query[q])
            {
                found.Add(new TextMatch(i, 1));
                q++;
            }
        }

        return q == query.Length ? found : null;
    }
}
