using System.Globalization;

namespace SpaceNotch.Core.Setup;

/// <summary>
/// Les mots de l'installeur et du désinstalleur, en français et en anglais.
/// La langue suit celle de Windows : français sur un Windows français, anglais
/// partout ailleurs. Même ton que la page du projet : peu de mots, jamais
/// techniques.
/// </summary>
public sealed record SetupText
{
    public required string Tagline { get; init; }
    public required string ForMe { get; init; }
    public required string ForMeDetail { get; init; }
    public required string ForEveryone { get; init; }
    public required string ForEveryoneDetail { get; init; }
    public required string StartWithWindows { get; init; }
    public required string DesktopShortcut { get; init; }
    public required string Install { get; init; }
    public required string Update { get; init; }
    public required string Reinstall { get; init; }
    public required string Replace { get; init; }
    public required string Cancel { get; init; }
    public required string Close { get; init; }
    public required string Retry { get; init; }
    public required string InstallFolderFormat { get; init; }
    public required string UpdateFormat { get; init; }
    public required string ReinstallFormat { get; init; }
    public required string DowngradeFormat { get; init; }
    public required string KeepsChoices { get; init; }
    public required string Preparing { get; init; }
    public required string Stopping { get; init; }
    public required string Copying { get; init; }
    public required string Shortcuts { get; init; }
    public required string Registering { get; init; }
    public required string Done { get; init; }
    public required string DoneDetail { get; init; }
    public required string Failed { get; init; }
    public required string ElevationDeclined { get; init; }
    public required string UninstallTitle { get; init; }
    public required string UninstallDetail { get; init; }
    public required string RemoveSettings { get; init; }
    public required string Uninstall { get; init; }
    public required string Removing { get; init; }
    public required string Removed { get; init; }
    public required string RemovedDetail { get; init; }
    public required string NotInstalled { get; init; }

    /// <summary>Texte d'une étape d'installation.</summary>
    public string For(InstallStep step) => step switch
    {
        InstallStep.Preparing => Preparing,
        InstallStep.Stopping => Stopping,
        InstallStep.Copying => Copying,
        InstallStep.Shortcuts => Shortcuts,
        InstallStep.Registering => Registering,
        _ => Done
    };

    /// <summary>Phrase d'en-tête selon ce qui est déjà installé.</summary>
    public string Headline(InstallKind kind, string? installed, string current) => kind switch
    {
        InstallKind.Update => Format(UpdateFormat, SetupVersion.Display(installed), SetupVersion.Display(current)),
        InstallKind.Reinstall => Format(ReinstallFormat, SetupVersion.Display(current)),
        InstallKind.Downgrade => Format(DowngradeFormat, SetupVersion.Display(installed)),
        _ => Tagline
    };

    /// <summary>Libellé du bouton principal selon ce qui est déjà installé.</summary>
    public string PrimaryAction(InstallKind kind) => kind switch
    {
        InstallKind.Update => Update,
        InstallKind.Reinstall => Reinstall,
        InstallKind.Downgrade => Replace,
        _ => Install
    };

    /// <summary>« Dans C:\… » : où l'application va s'installer.</summary>
    public string InstallFolder(string directory) => Format(InstallFolderFormat, directory);

    /// <summary>Textes pour une culture ; le français pour « fr », l'anglais sinon.</summary>
    public static SetupText For(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        return string.Equals(culture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase)
            ? French
            : English;
    }

    /// <summary>Textes dans la langue de l'interface de Windows.</summary>
    public static SetupText Current => For(CultureInfo.CurrentUICulture);

    private static string Format(string format, params object[] values)
        => string.Format(CultureInfo.CurrentCulture, format, values);

    public static SetupText French { get; } = new()
    {
        Tagline = "Un petit morceau de nuit en haut de votre écran.",
        ForMe = "Pour moi",
        ForMeDetail = "Sans droits d'administrateur",
        ForEveryone = "Pour tous",
        ForEveryoneDetail = "Tous les comptes de ce PC",
        StartWithWindows = "Lancer au démarrage de Windows",
        DesktopShortcut = "Raccourci sur le bureau",
        Install = "Installer",
        Update = "Mettre à jour",
        Reinstall = "Réinstaller",
        Replace = "Remplacer",
        Cancel = "Annuler",
        Close = "Fermer",
        Retry = "Réessayer",
        InstallFolderFormat = "Dans {0}",
        UpdateFormat = "Mise à jour {0} → {1}",
        ReinstallFormat = "La version {0} est déjà installée.",
        DowngradeFormat = "Une version plus récente ({0}) est installée.",
        KeepsChoices = "Vos réglages et vos choix sont conservés.",
        Preparing = "Préparation…",
        Stopping = "La notch se retire un instant…",
        Copying = "SpaceNotch s'installe…",
        Shortcuts = "Raccourcis…",
        Registering = "Presque prêt…",
        Done = "C'est prêt.",
        DoneDetail = "Levez les yeux : votre notch est là-haut.",
        Failed = "L'installation n'a pas abouti.",
        ElevationDeclined = "Pour installer pour tous, Windows doit donner son accord. Réessayez, ou installez pour vous seul.",
        UninstallTitle = "Désinstaller SpaceNotch ?",
        UninstallDetail = "La notch, ses raccourcis et son lancement au démarrage seront retirés.",
        RemoveSettings = "Supprimer aussi mes réglages",
        Uninstall = "Désinstaller",
        Removing = "La notch s'en va…",
        Removed = "SpaceNotch est désinstallée.",
        RemovedDetail = "À bientôt.",
        NotInstalled = "SpaceNotch n'est pas installée sur ce PC."
    };

    public static SetupText English { get; } = new()
    {
        Tagline = "A small piece of darkness at the top of your screen.",
        ForMe = "Just me",
        ForMeDetail = "No administrator rights",
        ForEveryone = "Everyone",
        ForEveryoneDetail = "Every account on this PC",
        StartWithWindows = "Start with Windows",
        DesktopShortcut = "Desktop shortcut",
        Install = "Install",
        Update = "Update",
        Reinstall = "Reinstall",
        Replace = "Replace",
        Cancel = "Cancel",
        Close = "Close",
        Retry = "Try again",
        InstallFolderFormat = "In {0}",
        UpdateFormat = "Update {0} → {1}",
        ReinstallFormat = "Version {0} is already installed.",
        DowngradeFormat = "A newer version ({0}) is installed.",
        KeepsChoices = "Your settings and choices are kept.",
        Preparing = "Getting ready…",
        Stopping = "The notch steps aside for a moment…",
        Copying = "Installing SpaceNotch…",
        Shortcuts = "Shortcuts…",
        Registering = "Almost there…",
        Done = "All set.",
        DoneDetail = "Look up: your notch is waiting.",
        Failed = "Installation didn't finish.",
        ElevationDeclined = "Installing for everyone needs Windows' permission. Try again, or install just for you.",
        UninstallTitle = "Uninstall SpaceNotch?",
        UninstallDetail = "The notch, its shortcuts and its start-up entry will be removed.",
        RemoveSettings = "Also remove my settings",
        Uninstall = "Uninstall",
        Removing = "The notch is leaving…",
        Removed = "SpaceNotch is uninstalled.",
        RemovedDetail = "See you soon.",
        NotInstalled = "SpaceNotch isn't installed on this PC."
    };
}
