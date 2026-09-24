namespace SpaceNotch.Core.Setup;

/// <summary>
/// La désinstallation efface le dossier de l'exécutable qui la mène — qui ne
/// peut pas s'effacer lui-même tant qu'il tourne. Elle confie donc la fin à
/// l'interpréteur de commandes : attendre deux secondes que le processus soit
/// sorti, puis supprimer — deux fois, la seconde rattrapant ce qu'un processus
/// encore en train de se fermer retenait. C'est la technique classique des
/// désinstalleurs sans service : rien ne reste, pas même une tâche planifiée.
/// </summary>
public static class SelfDelete
{
    /// <summary>
    /// Arguments de <c>cmd.exe</c>. Un chemin contenant un guillemet ou vide est
    /// écarté : il casserait la commande, et n'est jamais un de nos dossiers.
    /// </summary>
    public static string Arguments(IEnumerable<string> directories)
    {
        ArgumentNullException.ThrowIfNull(directories);

        List<string> removals = directories
            .Where(d => !string.IsNullOrWhiteSpace(d) && !d.Contains('"', StringComparison.Ordinal) && IsRooted(d))
            .Select(d => $"rd /s /q \"{d.TrimEnd('\\')}\" 2> nul")
            .ToList();

        const string Wait = "ping -n 3 127.0.0.1 > nul";
        var commands = new List<string> { Wait };
        commands.AddRange(removals);

        if (removals.Count > 0)
        {
            commands.Add(Wait);
            commands.AddRange(removals);
        }

        return "/d /c " + string.Join(" & ", commands);
    }

    /// <summary>Un chemin absolu à lettre de lecteur, profond d'au moins deux dossiers : jamais la racine d'un disque.</summary>
    private static bool IsRooted(string path)
        => path.Length > 3
            && char.IsAsciiLetter(path[0])
            && path[1] == ':'
            && path[2] == '\\'
            && path.TrimEnd('\\').Count(c => c == '\\') >= 2;
}
