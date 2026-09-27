using System;
using System.Text;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Titres qui se décodent (A1) : pendant 0,5 s, les lettres défilent en blocs
/// de pixels puis se fixent de gauche à droite. Déterministe : la même graine
/// donne les mêmes blocs, ce qui rend l'effet testable et sans scintillement
/// aléatoire d'une image à l'autre.
/// </summary>
public static class TextScramble
{
    /// <summary>Durée totale, en secondes.</summary>
    public const double Seconds = 0.5;

    private const string Blocks = "▖▗▘▙▚▛▜▝▞▟█▀▄▌▐";

    /// <summary>
    /// Image du titre à l'avancement <paramref name="progress"/> (0..1) : les
    /// lettres déjà fixées, puis des blocs. Les espaces restent des espaces,
    /// pour que la forme des mots se lise dès le début.
    /// </summary>
    public static string Frame(string target, double progress, int frame)
    {
        ArgumentNullException.ThrowIfNull(target);
        progress = Math.Clamp(progress, 0, 1);

        if (progress >= 1 || target.Length == 0)
        {
            return target;
        }

        int settled = (int)Math.Floor(target.Length * progress);
        var builder = new StringBuilder(target.Length);

        for (int i = 0; i < target.Length; i++)
        {
            char c = target[i];

            if (i < settled || char.IsWhiteSpace(c))
            {
                builder.Append(c);
                continue;
            }

            uint hash = (uint)((i * 73856093) ^ (frame * 19349663) ^ c);
            builder.Append(Blocks[(int)(hash % (uint)Blocks.Length)]);
        }

        return builder.ToString();
    }

    /// <summary>
    /// Vrai changement de titre ? Une même chaîne republiée ne rejoue rien ; un
    /// titre vide qui se remplit non plus (c'est une arrivée, pas un changement).
    /// </summary>
    public static bool ShouldPlay(string? before, string? after)
        => !string.IsNullOrEmpty(before)
            && !string.IsNullOrEmpty(after)
            && !string.Equals(before, after, StringComparison.Ordinal);
}
