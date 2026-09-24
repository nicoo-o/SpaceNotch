using System.Globalization;

namespace SpaceNotch.Core.Setup;

/// <summary>Une valeur de registre : texte (REG_SZ) ou nombre (REG_DWORD).</summary>
public sealed record RegistryValue(string Name, string? Text = null, int? Number = null);

/// <summary>SpaceNotch telle que Windows la connaît : ce que lit une mise à jour.</summary>
/// <param name="Version">Version installée.</param>
/// <param name="Directory">Dossier d'installation.</param>
/// <param name="Options">Choix faits à l'installation, repris par la mise à jour.</param>
public sealed record InstalledProduct(string Version, string Directory, InstallOptions Options)
{
    /// <summary>Exécutable installé.</summary>
    public string Executable => WindowsPath.Join(Directory, SetupIdentity.ExecutableName);
}

/// <summary>
/// L'entrée de Paramètres › Applications. Elle porte aussi les choix de
/// l'installation — portée, démarrage, raccourci — pour qu'une mise à jour les
/// reprenne sans les redemander.
/// </summary>
public static class UninstallEntry
{
    private const string ScopeValue = "SpaceNotchScope";
    private const string StartupValue = "SpaceNotchStartup";
    private const string DesktopValue = "SpaceNotchDesktop";

    /// <summary>Valeurs à écrire sous <see cref="SetupIdentity.UninstallKeyPath"/>.</summary>
    /// <param name="layout">Plan d'installation.</param>
    /// <param name="options">Choix de l'installation.</param>
    /// <param name="version">Version installée.</param>
    /// <param name="sizeBytes">Taille installée, pour l'estimation affichée par Windows.</param>
    /// <param name="installedOn">Date d'installation.</param>
    public static IReadOnlyList<RegistryValue> Values(
        InstallLayout layout,
        InstallOptions options,
        string version,
        long sizeBytes,
        DateOnly installedOn)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        // Toujours entre guillemets : Windows lit la commande telle quelle.
        string exe = $"\"{layout.Executable}\"";

        return
        [
            new RegistryValue("DisplayName", SetupIdentity.ProductName),
            new RegistryValue("DisplayVersion", version),
            new RegistryValue("Publisher", SetupIdentity.Publisher),
            new RegistryValue("DisplayIcon", $"{layout.Executable},0"),
            new RegistryValue("InstallLocation", layout.Directory),
            new RegistryValue("InstallDate", installedOn.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
            new RegistryValue("UninstallString", $"{exe} --uninstall"),
            new RegistryValue("QuietUninstallString", $"{exe} --uninstall --quiet"),
            new RegistryValue("URLInfoAbout", SetupIdentity.HomePage),
            new RegistryValue("HelpLink", SetupIdentity.HomePage),
            new RegistryValue("EstimatedSize", Number: (int)Math.Clamp((sizeBytes + 1023) / 1024, 0, int.MaxValue)),
            new RegistryValue("NoModify", Number: 1),
            new RegistryValue("NoRepair", Number: 1),
            new RegistryValue(ScopeValue, options.Scope == InstallScope.AllUsers ? "machine" : "user"),
            new RegistryValue(StartupValue, Number: options.StartWithWindows ? 1 : 0),
            new RegistryValue(DesktopValue, Number: options.DesktopShortcut ? 1 : 0)
        ];
    }

    /// <summary>
    /// Relit une entrée. Renvoie <c>null</c> si elle ne décrit pas une
    /// installation utilisable — dossier ou version manquants.
    /// </summary>
    /// <param name="read">Lecteur de valeur : nom → texte ou nombre, ou <c>null</c>.</param>
    /// <param name="scope">Ruche où l'entrée a été trouvée.</param>
    public static InstalledProduct? Read(Func<string, object?> read, InstallScope scope)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (read("InstallLocation") is not string directory || string.IsNullOrWhiteSpace(directory)
            || read("DisplayVersion") is not string version || string.IsNullOrWhiteSpace(version))
        {
            return null;
        }

        bool startup = read(StartupValue) is not int s || s != 0;
        bool desktop = read(DesktopValue) is not int d || d != 0;

        return new InstalledProduct(version, directory, new InstallOptions(scope, startup, desktop));
    }
}
