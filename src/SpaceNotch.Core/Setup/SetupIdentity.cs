namespace SpaceNotch.Core.Setup;

/// <summary>
/// Ce que Windows sait de SpaceNotch une fois installée : noms, dossiers,
/// clés. Un seul endroit, pour que l'installeur, le désinstalleur et la mise à
/// jour parlent exactement de la même chose. Voir ADR-022.
/// </summary>
public static class SetupIdentity
{
    /// <summary>Nom affiché dans Paramètres › Applications et sur les raccourcis.</summary>
    public const string ProductName = "SpaceNotch";

    /// <summary>Éditeur affiché dans Paramètres › Applications.</summary>
    public const string Publisher = "SpaceNotch";

    /// <summary>Nom de l'exécutable installé. L'installeur, lui, porte « Setup » dans son nom.</summary>
    public const string ExecutableName = "SpaceNotch.exe";

    /// <summary>Nom du processus de l'application, pour la fermer avant une mise à jour.</summary>
    public const string ProcessName = "SpaceNotch";

    /// <summary>Nom des raccourcis du menu Démarrer et du bureau.</summary>
    public const string ShortcutName = "SpaceNotch.lnk";

    /// <summary>Clé de désinstallation, sous HKCU ou HKLM selon la portée.</summary>
    public const string UninstallKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\SpaceNotch";

    /// <summary>Page du projet, liée depuis Paramètres › Applications.</summary>
    public const string HomePage = "https://github.com/nicoo-o/SpaceNotch";

    /// <summary>Description des raccourcis, lue par le Narrateur et en info-bulle.</summary>
    public const string ShortcutDescription = "SpaceNotch — une notch vivante en haut de l'écran";
}
