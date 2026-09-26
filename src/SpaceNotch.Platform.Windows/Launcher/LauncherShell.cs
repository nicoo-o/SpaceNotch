using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Les gestes du panneau d'actions : ouvrir, ouvrir l'emplacement, exécuter en
/// administrateur, désinstaller. Chacun renvoie faux plutôt que de lever : un
/// lancement refusé (élévation annulée, cible disparue) n'est pas une panne.
/// </summary>
public static class LauncherShell
{
    private const int ErrorCancelled = 1223;

    /// <summary>
    /// Ouvre une cible : application du dossier Applications
    /// (<c>shell:AppsFolder\…</c>), fichier, page <c>ms-settings:</c> ou adresse web.
    /// </summary>
    public static bool Open(string target, bool asAdministrator = false)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        try
        {
            ProcessStartInfo start = target.StartsWith(@"shell:AppsFolder\", StringComparison.OrdinalIgnoreCase)
                ? new ProcessStartInfo("explorer.exe", "\"" + target + "\"")
                : new ProcessStartInfo(target);

            start.UseShellExecute = true;

            if (asAdministrator)
            {
                start.Verb = "runas";
            }

            using Process? _ = Process.Start(start);
            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return false;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            return false;
        }
    }

    /// <summary>Montre l'élément dans l'Explorateur, sélectionné dans son dossier.</summary>
    public static bool OpenLocation(string target)
    {
        if (string.IsNullOrWhiteSpace(target))
        {
            return false;
        }

        string? path = ExecutablePathOf(target);

        try
        {
            string arguments = path is not null && File.Exists(path)
                ? "/select,\"" + path + "\""
                : "\"shell:AppsFolder\"";

            using Process? _ = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Ouvre Paramètres › Applications installées, là où Windows désinstalle.</summary>
    public static bool Uninstall() => Open("ms-settings:appsfeatures");

    /// <summary>
    /// Le fichier derrière une cible, quand il y en a un : chemin direct, ou
    /// chemin d'une application de bureau dans le dossier Applications (la
    /// partie après <c>shell:AppsFolder\</c>, parfois préfixée d'un dossier
    /// connu). <c>null</c> pour une application du Store.
    /// </summary>
    public static string? ExecutablePathOf(string target)
    {
        const string Prefix = @"shell:AppsFolder\";

        string path = target.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            ? target[Prefix.Length..]
            : target;

        if (path.Contains('!', StringComparison.Ordinal))
        {
            return null;
        }

        // « {GUID de dossier connu}\sous\chemin.exe »
        if (path.StartsWith('{') && path.IndexOf('}', StringComparison.Ordinal) is > 0 and var close)
        {
            string? root = KnownFolderPath(path[1..close]);
            path = root is null ? path : Path.Join(root, path[(close + 1)..].TrimStart('\\'));
        }

        return Path.IsPathRooted(path) ? path : null;
    }

    private static string? KnownFolderPath(string guid)
    {
        if (!Guid.TryParse(guid, out Guid id))
        {
            return null;
        }

        Environment.SpecialFolder? folder = id.ToString().ToUpperInvariant() switch
        {
            "6D809377-6AF0-444B-8957-A3773F02200E" => Environment.SpecialFolder.ProgramFiles,
            "7C5A40EF-A0FB-4BFC-874A-C0F2E0B9FA8E" => Environment.SpecialFolder.ProgramFilesX86,
            "1AC14E77-02E7-4E5D-B744-2EB1AE5198B7" => Environment.SpecialFolder.System,
            "F38BF404-1D43-42F2-9305-67DE0B28FC23" => Environment.SpecialFolder.Windows,
            "F1B32785-6FBA-4FCF-9D55-7B8E7F157091" => Environment.SpecialFolder.LocalApplicationData,
            "3EB685DB-65F9-4CF6-A03A-E3EF65729F3D" => Environment.SpecialFolder.ApplicationData,
            _ => null
        };

        return folder is null ? null : Environment.GetFolderPath(folder.Value);
    }
}
