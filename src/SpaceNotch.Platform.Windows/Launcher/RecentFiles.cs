using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceNotch.Core.Launcher;
using SpaceNotch.Platform.Windows.Setup;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Les fichiers ouverts récemment, d'après le dossier « Récents » de Windows :
/// des raccourcis dont on lit la cible. Les dossiers et les cibles disparues
/// sont écartés.
/// </summary>
public static class RecentFiles
{
    public const int MaxFiles = 30;

    public static IReadOnlyList<LauncherCandidate> Load()
    {
        string recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);

        if (string.IsNullOrEmpty(recent) || !Directory.Exists(recent))
        {
            return [];
        }

        var results = new List<LauncherCandidate>();

        IEnumerable<FileInfo> links;

        try
        {
            links = new DirectoryInfo(recent)
                .EnumerateFiles("*.lnk")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Take(MaxFiles * 2)
                .ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        foreach (FileInfo link in links)
        {
            string? target = ShellLink.ResolveTarget(link.FullName);

            if (string.IsNullOrEmpty(target) || !File.Exists(target))
            {
                continue;
            }

            string folder = Path.GetFileName(Path.GetDirectoryName(target)) ?? string.Empty;

            results.Add(new LauncherCandidate(
                LauncherResultKind.File,
                Path.GetFileName(target),
                folder,
                target,
                IconPath: target));

            if (results.Count >= MaxFiles)
            {
                break;
            }
        }

        return results;
    }
}
