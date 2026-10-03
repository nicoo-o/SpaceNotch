using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Launcher;

namespace SpaceNotch.Core.Accessibility;

/// <summary>
/// Recherche des réglages (phase C) : une carte se trouve par son texte
/// français <em>et</em> par ce qu'elle affiche dans la langue de l'interface,
/// sans tenir compte des accents ni de la casse, et chaque mot de la requête
/// doit y figurer. « luminosite ecran » trouve « Luminosité de l'écran »,
/// « brightness » aussi.
/// </summary>
public static class SettingsSearch
{
    /// <summary>Texte indexé d'une carte : ses morceaux, repliés (minuscules, sans accents).</summary>
    public static string Index(IEnumerable<string?> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return LauncherRanking.Fold(string.Join(' ', parts.Where(p => !string.IsNullOrWhiteSpace(p))));
    }

    /// <summary>Vrai si chaque mot de la requête figure dans le texte indexé.</summary>
    public static bool Matches(string index, string query)
    {
        ArgumentNullException.ThrowIfNull(index);

        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        return LauncherRanking.Fold(query)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .All(word => index.Contains(word, StringComparison.Ordinal));
    }
}
