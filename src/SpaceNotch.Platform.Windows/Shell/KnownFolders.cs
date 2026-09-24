using System;
using System.IO;
using System.Runtime.InteropServices;
using SpaceNotch.Platform.Windows.Win32;

namespace SpaceNotch.Platform.Windows.Shell;

/// <summary>
/// Dossiers connus de l'utilisateur.
/// </summary>
public static class KnownFolders
{
    /// <summary>FOLDERID_Downloads.</summary>
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// Dossier Téléchargements réel de l'utilisateur.
    ///
    /// Il n'est pas toujours sous le profil : Windows permet de le déplacer sur
    /// un autre disque, et c'est fréquent. Seul le shell connaît son
    /// emplacement ; le chemin conventionnel n'est qu'un repli.
    /// </summary>
    public static string Downloads()
    {
        try
        {
            if (NativeMethods.SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out IntPtr path) == 0)
            {
                try
                {
                    string? resolved = Marshal.PtrToStringUni(path);

                    if (!string.IsNullOrEmpty(resolved))
                    {
                        return resolved;
                    }
                }
                finally
                {
                    Marshal.FreeCoTaskMem(path);
                }
            }
        }
        catch (Exception)
        {
            // Hors de Windows, ou shell indisponible : le repli suffit.
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");
    }
}
