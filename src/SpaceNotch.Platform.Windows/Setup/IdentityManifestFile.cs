using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SpaceNotch.Core.Setup;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>
/// Allume ou éteint l'élément d'identité dans un exécutable sur disque (voir
/// <see cref="IdentityManifest"/>). Lecture par blocs : l'exécutable unique
/// pèse environ 140 Mo, il n'est jamais chargé entier en mémoire.
/// </summary>
public static class IdentityManifestFile
{
    private const int Block = 4 * 1024 * 1024;

    /// <summary>
    /// Met l'élément dans l'état voulu. Sans écriture s'il y est déjà (un
    /// exécutable en cours d'exécution ne peut pas être modifié).
    /// </summary>
    /// <returns>Vrai si l'exécutable est dans l'état voulu à la sortie.</returns>
    public static bool Set(string path, bool on, Action<string>? log = null)
    {
        try
        {
            List<IdentityManifest.Element> elements = Locate(path);

            if (elements.Count != 1)
            {
                log?.Invoke($"[IDENTITÉ] Manifeste : {elements.Count} élément(s) d'identité trouvé(s) dans {path}, 1 attendu.");
                return false;
            }

            IdentityManifest.Element element = elements[0];

            if (element.On == on)
            {
                return true;
            }

            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var bytes = new byte[element.Length];
            stream.Seek(element.Offset, SeekOrigin.Begin);
            stream.ReadExactly(bytes);

            if (!IdentityManifest.Rewrite(bytes, on))
            {
                return false;
            }

            stream.Seek(element.Offset, SeekOrigin.Begin);
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);

            log?.Invoke($"[IDENTITÉ] Manifeste {(on ? "allumé" : "éteint")} : {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"[IDENTITÉ] Manifeste non modifié ({path}) : {ex.Message}");
            return false;
        }
    }

    /// <summary>Vrai si l'exécutable déclare l'identité (élément allumé).</summary>
    public static bool IsOn(string path)
    {
        try
        {
            List<IdentityManifest.Element> elements = Locate(path);
            return elements.Count == 1 && elements[0].On;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static List<IdentityManifest.Element> Locate(string path)
    {
        // Les blocs se chevauchent de la longueur maximale d'un élément, pour
        // qu'aucun ne soit coupé en deux ; un élément vu dans deux blocs n'est
        // compté qu'une fois.
        var found = new Dictionary<long, IdentityManifest.Element>();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[Block + IdentityManifest.MaxLength];
        long position = 0;

        while (true)
        {
            stream.Seek(position, SeekOrigin.Begin);
            int read = stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);

            if (read == 0)
            {
                break;
            }

            foreach (IdentityManifest.Element element in IdentityManifest.Find(buffer, read, position))
            {
                found[element.Offset] = element;
            }

            if (read < buffer.Length)
            {
                break;
            }

            position += Block;
        }

        return found.Values.OrderBy(e => e.Offset).ToList();
    }
}
