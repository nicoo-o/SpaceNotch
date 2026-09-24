using System;
using System.IO;

namespace SpaceNotch.Infrastructure.Config;

/// <summary>
/// Reprise des données écrites sous l'ancien nom du produit.
///
/// <para>
/// Le projet s'appelait NotchFlow. Ses préférences, son journal et ses greffons
/// vivaient donc sous <c>%AppData%\NotchFlow</c> et <c>%LocalAppData%\NotchFlow</c>.
/// Un utilisateur qui met à jour ne doit perdre ni ses réglages ni ses greffons :
/// au premier lancement, ce qui existe sous l'ancien nom est <em>copié</em> sous
/// le nouveau, jamais déplacé — une version précédente relancée par erreur
/// retrouve encore ses fichiers.
/// </para>
///
/// <para>La copie n'a lieu qu'une fois : dès que la destination existe, elle fait foi.</para>
/// </summary>
public static class LegacyMigration
{
    /// <summary>Ancien nom du produit, tel qu'il apparaissait dans les chemins.</summary>
    public const string LegacyProductName = "NotchFlow";

    /// <summary>
    /// Copie <paramref name="legacyDirectory"/> vers <paramref name="targetDirectory"/>
    /// si la destination n'existe pas encore et que la source existe.
    /// </summary>
    /// <returns>Vrai si une copie a eu lieu.</returns>
    public static bool CopyDirectoryOnce(string legacyDirectory, string targetDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(legacyDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetDirectory);

        try
        {
            if (Directory.Exists(targetDirectory) || !Directory.Exists(legacyDirectory))
            {
                return false;
            }

            CopyRecursive(new DirectoryInfo(legacyDirectory), targetDirectory);
            return true;
        }
        catch (Exception)
        {
            // Une reprise impossible — droits, disque plein — laisse l'application
            // démarrer sur des valeurs neuves : c'est préférable à ne pas démarrer.
            return false;
        }
    }

    /// <summary>Emplacement équivalent sous l'ancien nom : même parent, ancien nom de dossier.</summary>
    public static string LegacySibling(string root)
        => Path.Combine(root, LegacyProductName);

    private static void CopyRecursive(DirectoryInfo source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (FileInfo file in source.GetFiles())
        {
            file.CopyTo(Path.Combine(destination, file.Name), overwrite: false);
        }

        foreach (DirectoryInfo child in source.GetDirectories())
        {
            CopyRecursive(child, Path.Combine(destination, child.Name));
        }
    }
}
