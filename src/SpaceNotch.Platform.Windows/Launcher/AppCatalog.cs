using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Launcher;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Les applications installées, comme les liste le menu Démarrer : le dossier
/// virtuel <c>shell:AppsFolder</c>, qui réunit applications de bureau et
/// applications du Store (Calculatrice, Paramètres, Photos, Terminal…). C'est
/// la source qu'utilisent PowerToys Run et la Palette de commandes. Les
/// raccourcis de maintenance — désinstaller, lisez-moi, aide, documentation —
/// sont écartés.
/// </summary>
public static class AppCatalog
{
    private static readonly string[] NoiseWords =
    [
        "uninstall", "désinstaller", "desinstaller", "readme", "lisez-moi", "lisezmoi",
        "documentation", "release notes", "notes de version", "manuel", "manual",
        "website", "site web", "site internet", "support", "license", "licence", "migrer", "migrate",
        "restaurer les paramètres", "reset settings", "à propos de", "about "
    ];

    private static readonly string[] NoiseExtensions =
    [
        ".url", ".chm", ".txt", ".pdf", ".html", ".htm", ".hlp", ".rtf", ".md", ".log", ".ini", ".xml"
    ];

    /// <summary>
    /// Lit le catalogue sur un fil STA dédié : les objets du Shell sont prévus
    /// pour lui. Renvoie une liste vide si le Shell refuse, jamais une exception.
    /// </summary>
    public static Task<IReadOnlyList<LauncherCandidate>> LoadAsync(string subtitle, string storeSubtitle, Action<string>? log = null)
    {
        var completion = new TaskCompletionSource<IReadOnlyList<LauncherCandidate>>();

        var thread = new Thread(() =>
        {
            try
            {
                completion.SetResult(Enumerate(subtitle, storeSubtitle));
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException)
            {
                log?.Invoke($"[LAUNCHER] Catalogue des applications illisible : {ex.Message}");
                completion.SetResult([]);
            }
        })
        {
            IsBackground = true,
            Name = "SpaceNotch.AppCatalog"
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return completion.Task;
    }

    /// <summary>Vrai si ce nom ou cette cible est un raccourci de maintenance plutôt qu'une application.</summary>
    public static bool IsNoise(string name, string parsingName)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(parsingName);

        string lower = name.ToLowerInvariant();

        if (NoiseWords.Any(w => lower.Contains(w, StringComparison.Ordinal)))
        {
            return true;
        }

        string extension = Path.GetExtension(parsingName.Split('!')[0]).ToLowerInvariant();
        return NoiseExtensions.Contains(extension);
    }

    /// <summary>Ce qu'on passe à l'Explorateur pour lancer une application du dossier Applications.</summary>
    public static string LaunchTargetOf(string parsingName) => @"shell:AppsFolder\" + parsingName;

    private static List<LauncherCandidate> Enumerate(string subtitle, string storeSubtitle)
    {
        var results = new List<LauncherCandidate>();

        Guid folderId = ShellItems.FolderIdAppsFolder;
        Guid iidItem = ShellItems.IidShellItem;
        Marshal.ThrowExceptionForHR(ShellItems.SHGetKnownFolderItem(in folderId, 0, IntPtr.Zero, in iidItem, out IntPtr folderPtr));
        var folder = ShellItems.Wrap<ShellItems.IShellItem>(folderPtr);

        Guid bhid = ShellItems.BhidEnumItems;
        Guid iidEnum = ShellItems.IidEnumShellItems;
        folder.BindToHandler(IntPtr.Zero, in bhid, in iidEnum, out IntPtr enumPtr);
        var items = ShellItems.Wrap<ShellItems.IEnumShellItems>(enumPtr);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (items.Next(1, out IntPtr itemPtr, out uint fetched) == 0 && fetched == 1)
        {
            var item = ShellItems.Wrap<ShellItems.IShellItem>(itemPtr);

            item.GetDisplayName(ShellItems.SigdnNormalDisplay, out IntPtr namePtr);
            item.GetDisplayName(ShellItems.SigdnParentRelativeParsing, out IntPtr parsingPtr);

            string? name = ShellItems.TakeString(namePtr);
            string? parsing = ShellItems.TakeString(parsingPtr);

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(parsing) || IsNoise(name, parsing))
            {
                continue;
            }

            // Un même nom peut revenir sous deux cibles (raccourci utilisateur et
            // raccourci commun) : une seule ligne.
            if (!seen.Add(name))
            {
                continue;
            }

            string target = LaunchTargetOf(parsing);
            bool store = parsing.Contains('!', StringComparison.Ordinal);

            results.Add(new LauncherCandidate(
                LauncherResultKind.Application,
                name,
                store ? storeSubtitle : subtitle,
                target,
                IconPath: target));
        }

        return results;
    }
}
