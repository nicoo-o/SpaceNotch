using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Rémanence et encre : deux façons de montrer ce qui vient de changer.
///
/// <para>
/// <b>Rémanence phosphore (A1).</b> Un pixel qui s'éteint ne disparaît pas
/// net : il décroît en cyan, comme le phosphore d'un vieil écran. Sur OLED, où
/// le noir est vraiment éteint, l'effet est doux.
/// </para>
///
/// <para>
/// <b>Rafraîchissement d'encre (A3).</b> Quand une valeur change (température,
/// pourcentage), seuls les caractères touchés s'inversent un instant, comme
/// sur une liseuse. Un seul flash, loin de la limite de trois par seconde.
/// </para>
/// </summary>
public static class Afterglow
{
    /// <summary>Durée de la décroissance phosphore, en millisecondes.</summary>
    public const int GlowMilliseconds = 160;

    /// <summary>Opacité de départ du pixel rémanent.</summary>
    public const double GlowOpacity = 0.9;

    /// <summary>Durée de l'inversion d'encre, en millisecondes.</summary>
    public const int InkMilliseconds = 80;

    /// <summary>Pixels allumés dans <paramref name="before"/> et éteints dans <paramref name="after"/>.</summary>
    public static IReadOnlyList<int> Fading(IReadOnlyList<bool> before, IReadOnlyList<bool> after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var fading = new List<int>();
        int count = Math.Min(before.Count, after.Count);

        for (int i = 0; i < count; i++)
        {
            if (before[i] && !after[i])
            {
                fading.Add(i);
            }
        }

        return fading;
    }

    /// <summary>
    /// Plage de caractères à inverser entre deux valeurs : du premier caractère
    /// qui diffère au dernier, comptés sur la nouvelle valeur. Vide si rien ne
    /// change, ou s'il n'y avait pas de valeur avant (une apparition n'est pas
    /// un changement).
    /// </summary>
    public static (int Start, int Length) InkSpan(string? before, string after)
    {
        ArgumentNullException.ThrowIfNull(after);

        if (string.IsNullOrEmpty(before) || string.Equals(before, after, StringComparison.Ordinal) || after.Length == 0)
        {
            return (0, 0);
        }

        int start = 0;

        while (start < before.Length && start < after.Length && before[start] == after[start])
        {
            start++;
        }

        int endBefore = before.Length - 1, endAfter = after.Length - 1;

        while (endBefore >= start && endAfter >= start && before[endBefore] == after[endAfter])
        {
            endBefore--;
            endAfter--;
        }

        if (endAfter < start)
        {
            // Un caractère retiré : on inverse celui qui prend sa place.
            return (Math.Min(start, after.Length - 1), 1);
        }

        return (start, endAfter - start + 1);
    }
}
