using System;
using System.Collections.Generic;

namespace SpaceNotch_App.Startup;

/// <summary>
/// Options acceptées au lancement.
///
/// Elles existent parce que l'application peut être démarrée de trois manières :
/// par l'utilisateur, par Windows au démarrage de la session, ou volontairement
/// sur les réglages. Chacune doit être reconnaissable sans ambiguïté — et sans
/// qu'un argument inconnu fasse échouer le démarrage.
/// </summary>
/// <param name="OpenSettings">Ouvre la fenêtre de réglages après l'Island.</param>
/// <param name="StartedByWindows">
/// Vrai lorsque le lancement provient de l'inscription au démarrage. Rien n'est
/// fait différemment pour autant : l'Island doit se comporter de façon identique,
/// sinon un défaut de démarrage ne serait jamais reproductible à la main.
/// </param>
internal sealed record CommandLineOptions(bool OpenSettings, bool StartedByWindows)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        bool openSettings = false;
        bool startedByWindows = false;

        foreach (string argument in arguments)
        {
            if (Matches(argument, "--settings"))
            {
                openSettings = true;
            }
            else if (Matches(argument, "--startup"))
            {
                startedByWindows = true;
            }
        }

        return new CommandLineOptions(openSettings, startedByWindows);
    }

    /// <summary>
    /// Compare un argument en tolérant la casse et les tirets, sans jamais lever :
    /// un argument inconnu est simplement ignoré.
    /// </summary>
    private static bool Matches(string argument, string expected)
        => !string.IsNullOrWhiteSpace(argument)
            && string.Equals(argument.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
