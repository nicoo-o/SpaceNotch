namespace SpaceNotch.Core.Setup;

/// <summary>
/// Les dossiers de Windows dont l'installation dépend. Lus par la plateforme,
/// passés ici : le plan d'installation se calcule — et se teste — sans Windows.
/// </summary>
/// <param name="LocalAppData">%LocalAppData%, pour l'installation personnelle.</param>
/// <param name="RoamingAppData">%AppData%, où vivent les réglages.</param>
/// <param name="ProgramFiles">Program Files, pour l'installation pour tous.</param>
/// <param name="UserPrograms">Menu Démarrer de l'utilisateur.</param>
/// <param name="CommonPrograms">Menu Démarrer commun.</param>
/// <param name="UserDesktop">Bureau de l'utilisateur.</param>
/// <param name="CommonDesktop">Bureau commun.</param>
/// <param name="Temp">Dossier temporaire, où l'exécutable unique s'extrait.</param>
public sealed record SystemFolders(
    string LocalAppData,
    string RoamingAppData,
    string ProgramFiles,
    string UserPrograms,
    string CommonPrograms,
    string UserDesktop,
    string CommonDesktop,
    string Temp);

/// <summary>
/// Où SpaceNotch s'installe, pour une portée donnée.
///
/// <para>
/// Pour soi : <c>%LocalAppData%\Programs\SpaceNotch</c>, comme VS Code,
/// Discord ou Spotify — aucun droit d'administrateur, rien hors du profil.
/// Pour tous : <c>Program Files\SpaceNotch</c>, raccourcis communs.
/// </para>
/// </summary>
/// <param name="Scope">Portée.</param>
/// <param name="Directory">Dossier d'installation.</param>
/// <param name="Executable">Exécutable installé.</param>
/// <param name="StartMenuShortcut">Raccourci du menu Démarrer.</param>
/// <param name="DesktopShortcut">Raccourci du bureau.</param>
public sealed record InstallLayout(
    InstallScope Scope,
    string Directory,
    string Executable,
    string StartMenuShortcut,
    string DesktopShortcut)
{
    /// <summary>Vrai quand l'installation exige les droits d'administrateur.</summary>
    public bool RequiresElevation => Scope == InstallScope.AllUsers;

    /// <summary>Plan d'installation pour une portée.</summary>
    public static InstallLayout For(InstallScope scope, SystemFolders folders)
    {
        ArgumentNullException.ThrowIfNull(folders);

        string directory = scope == InstallScope.AllUsers
            ? WindowsPath.Join(folders.ProgramFiles, SetupIdentity.ProductName)
            : WindowsPath.Join(folders.LocalAppData, "Programs", SetupIdentity.ProductName);

        string programs = scope == InstallScope.AllUsers ? folders.CommonPrograms : folders.UserPrograms;
        string desktop = scope == InstallScope.AllUsers ? folders.CommonDesktop : folders.UserDesktop;

        return new InstallLayout(
            scope,
            directory,
            WindowsPath.Join(directory, SetupIdentity.ExecutableName),
            WindowsPath.Join(programs, SetupIdentity.ShortcutName),
            WindowsPath.Join(desktop, SetupIdentity.ShortcutName));
    }

    /// <summary>
    /// Ce que la désinstallation efface, une fois le processus terminé : le
    /// dossier d'installation, le cache d'extraction de l'exécutable unique et,
    /// sur demande, les réglages et journaux. Les greffons restent : ce sont les
    /// fichiers de l'utilisateur.
    /// </summary>
    public IReadOnlyList<string> LeftoversAfterExit(SystemFolders folders, bool removeSettings)
    {
        ArgumentNullException.ThrowIfNull(folders);

        var paths = new List<string>
        {
            Directory,

            // Chaque exécutable unique s'extrait sous %TEMP%\.net\<son nom> :
            // l'application installée, et l'installeur qui l'a posée.
            WindowsPath.Join(folders.Temp, ".net", SetupIdentity.ProcessName),
            WindowsPath.Join(folders.Temp, ".net", SetupIdentity.ProcessName + "-Setup")
        };

        if (removeSettings)
        {
            paths.Add(WindowsPath.Join(folders.RoamingAppData, SetupIdentity.ProductName));
            paths.Add(WindowsPath.Join(folders.LocalAppData, SetupIdentity.ProductName, "logs"));
        }

        return paths;
    }
}

/// <summary>Chemins Windows, assemblés de la même façon sur toutes les machines — tests compris.</summary>
public static class WindowsPath
{
    /// <summary>Joint des segments par une barre oblique inverse, sans en doubler aucune.</summary>
    public static string Join(params string[] segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        var parts = new List<string>(segments.Length);

        for (int i = 0; i < segments.Length; i++)
        {
            string segment = segments[i].Replace('/', '\\');
            segment = i == 0 ? segment.TrimEnd('\\') : segment.Trim('\\');

            if (segment.Length > 0)
            {
                parts.Add(segment);
            }
        }

        return string.Join('\\', parts);
    }

    /// <summary>Vrai si <paramref name="path"/> est <paramref name="directory"/> ou se trouve dessous.</summary>
    public static bool IsUnder(string path, string directory)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(directory);

        string p = path.Replace('/', '\\').TrimEnd('\\');
        string d = directory.Replace('/', '\\').TrimEnd('\\');

        return p.Equals(d, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(d + "\\", StringComparison.OrdinalIgnoreCase);
    }
}
