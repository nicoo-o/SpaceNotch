using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace SpaceNotch.Infrastructure.Plugins;

/// <summary>
/// Liste d'autorisation des greffons : un assemblage n'est chargé que si
/// l'utilisateur l'a approuvé, et tel qu'il l'a approuvé.
///
/// <para>
/// Un greffon est du code qui s'exécute avec les droits de SpaceNotch. Avant,
/// tout <c>.dll</c> déposé dans le dossier était chargé au démarrage : n'importe
/// quel programme capable d'écrire dans le profil de l'utilisateur pouvait s'y
/// greffer. Désormais chaque greffon est approuvé par son nom <em>et</em> son
/// empreinte SHA-256 : un fichier remplacé ou modifié redevient « en attente ».
/// </para>
/// </summary>
public sealed class PluginAllowlist
{
    private readonly IReadOnlyDictionary<string, string> _approved;

    /// <param name="approved">Nom de fichier → empreinte SHA-256 (hexadécimal), tels qu'enregistrés dans les réglages.</param>
    public PluginAllowlist(IReadOnlyDictionary<string, string> approved)
    {
        ArgumentNullException.ThrowIfNull(approved);
        _approved = approved;
    }

    /// <summary>Vrai si ce fichier précis a été approuvé.</summary>
    public bool IsAllowed(string assemblyPath)
    {
        string name = Path.GetFileName(assemblyPath);

        return _approved.TryGetValue(name, out string? expected)
            && Hash(assemblyPath) is { } actual
            && string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Empreinte SHA-256 d'un fichier, ou <c>null</c> s'il est illisible.</summary>
    public static string? Hash(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
