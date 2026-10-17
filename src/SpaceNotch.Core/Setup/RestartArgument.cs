using System;
using System.Collections.Generic;
using System.Globalization;

namespace SpaceNotch.Core.Setup;

/// <summary>
/// Redémarrer la notch (n° 12) : la nouvelle est lancée tout de suite avec le
/// numéro du processus qu'elle remplace, et attend qu'il soit sorti avant de
/// réclamer la place d'instance unique. Un simple délai ne suffisait pas : la
/// fermeture peut durer plus longtemps, la nouvelle trouvait la place prise et
/// se retirait, puis l'ancienne sortait — plus de notch du tout.
/// </summary>
public static class RestartArgument
{
    private const string Prefix = "--after-pid=";

    /// <summary>Attente maximale de la sortie de l'ancienne notch.</summary>
    public static readonly TimeSpan MaximumWait = TimeSpan.FromSeconds(15);

    /// <summary>L'argument à passer à la nouvelle notch.</summary>
    public static string For(int processId) => Prefix + processId.ToString(CultureInfo.InvariantCulture);

    /// <summary>Le processus à attendre, ou <c>null</c> sans argument valide.</summary>
    public static int? Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        foreach (string argument in arguments)
        {
            if (argument.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
                && int.TryParse(argument.AsSpan(Prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out int id)
                && id > 0)
            {
                return id;
            }
        }

        return null;
    }
}
