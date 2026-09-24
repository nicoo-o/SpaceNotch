using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SpaceNotch.Features.Downloads;

/// <summary>Un téléchargement en cours, vu depuis le dossier Téléchargements.</summary>
/// <param name="TemporaryPath">Chemin du fichier partiel du navigateur.</param>
/// <param name="DisplayName">Nom lisible : le nom final, sans l'extension temporaire.</param>
/// <param name="BytesReceived">Octets déjà écrits.</param>
public sealed record DownloadProgress(string TemporaryPath, string DisplayName, long BytesReceived);

/// <summary>Ce que le tracker conclut d'un changement du dossier.</summary>
public enum DownloadChange
{
    None = 0,
    Started = 1,
    Progressed = 2,
    Completed = 3,
    Cancelled = 4
}

/// <summary>
/// Suivi des téléchargements d'après les fichiers partiels des navigateurs.
///
/// <para>
/// <b>Pourquoi le dossier et pas les navigateurs.</b> Windows n'expose aucune API
/// commune des téléchargements, et chaque navigateur a la sienne — quand il en a
/// une. Mais tous écrivent un fichier partiel dans le dossier Téléchargements,
/// avec une extension reconnaissable, puis le renomment à la fin. Observer ce
/// dossier couvre donc Chrome, Edge, Brave, Vivaldi, Opera, Firefox et les
/// autres, sans extension de navigateur, sans scrutation, et sans rien lire du
/// contenu des fichiers.
/// </para>
///
/// <para>
/// La classe est pure : elle reçoit des événements de fichiers et rend une
/// conclusion. C'est ce qui permet de vérifier le comportement de chaque
/// navigateur sans en lancer un.
/// </para>
/// </summary>
public sealed class DownloadTracker
{
    /// <summary>
    /// Extensions des fichiers partiels : Chromium (Chrome, Edge, Brave,
    /// Vivaldi), Firefox, Opera, Safari et assimilés.
    /// </summary>
    public static IReadOnlyList<string> TemporaryExtensions { get; } =
        [".crdownload", ".part", ".opdownload", ".download", ".partial"];

    /// <summary>Nom provisoire de Chromium avant que l'utilisateur ne confirme : « Unconfirmed 123456 ».</summary>
    private static readonly Regex Unconfirmed = new(@"^Unconfirmed \d+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private readonly Dictionary<string, DownloadProgress> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Téléchargements en cours.</summary>
    public IReadOnlyCollection<DownloadProgress> Active => _active.Values;

    /// <summary>Dernier téléchargement terminé : son chemin final.</summary>
    public string? LastCompletedPath { get; private set; }

    /// <summary>Vrai si le chemin est un fichier partiel de navigateur.</summary>
    public static bool IsTemporary(string path)
        => TemporaryExtensions.Any(ext => path.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

    /// <summary>Nom lisible d'un fichier partiel.</summary>
    public static string DisplayNameOf(string temporaryPath)
    {
        string name = FileNameOf(temporaryPath);
        string? extension = TemporaryExtensions.FirstOrDefault(ext => name.EndsWith(ext, StringComparison.OrdinalIgnoreCase));

        if (extension is not null)
        {
            name = name[..^extension.Length];
        }

        return name.Length == 0 || Unconfirmed.IsMatch(name) ? "Téléchargement" : name;
    }

    /// <summary>
    /// Nom de fichier d'un chemin, quel que soit son séparateur : le suivi reste
    /// une fonction pure, vérifiable sur n'importe quelle machine.
    /// </summary>
    public static string FileNameOf(string path)
    {
        int separator = path.LastIndexOfAny(['\\', '/']);

        return separator < 0 ? path : path[(separator + 1)..];
    }

    /// <summary>Un fichier est apparu, ou a grossi.</summary>
    public DownloadChange OnWritten(string path, long size)
    {
        if (!IsTemporary(path))
        {
            return DownloadChange.None;
        }

        bool known = _active.ContainsKey(path);
        _active[path] = new DownloadProgress(path, DisplayNameOf(path), Math.Max(0, size));

        return known ? DownloadChange.Progressed : DownloadChange.Started;
    }

    /// <summary>
    /// Un fichier a été renommé. De partiel à partiel, c'est un téléchargement
    /// qui change de nom (Chromium, après confirmation) ; de partiel à final,
    /// c'est la fin.
    /// </summary>
    public DownloadChange OnRenamed(string oldPath, string newPath, long size)
    {
        if (!_active.Remove(oldPath, out DownloadProgress? previous) && !IsTemporary(oldPath))
        {
            return DownloadChange.None;
        }

        if (IsTemporary(newPath))
        {
            _active[newPath] = new DownloadProgress(
                newPath,
                DisplayNameOf(newPath),
                Math.Max(size, previous?.BytesReceived ?? 0));

            return previous is null ? DownloadChange.Started : DownloadChange.Progressed;
        }

        LastCompletedPath = newPath;
        return DownloadChange.Completed;
    }

    /// <summary>Un fichier a disparu : un fichier partiel supprimé est un téléchargement annulé.</summary>
    public DownloadChange OnDeleted(string path)
        => _active.Remove(path) ? DownloadChange.Cancelled : DownloadChange.None;

    /// <summary>Taille lisible, en unités décimales comme l'explorateur de fichiers.</summary>
    public static string FormatSize(long bytes)
    {
        string[] units = ["o", "Ko", "Mo", "Go", "To"];
        double value = bytes;
        int unit = 0;

        while (value >= 1000 && unit < units.Length - 1)
        {
            value /= 1000;
            unit++;
        }

        return unit == 0
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{value:0} {units[unit]}")
            : string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{value:0.#} {units[unit]}");
    }
}
