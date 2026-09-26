using System;
using System.Runtime.InteropServices;
using System.Text;

namespace SpaceNotch.Platform.Windows.Clipboard;

/// <summary>
/// Lecture et écriture du presse-papier texte.
///
/// Aucune donnée n'est lue tant que l'appelant ne le demande pas explicitement :
/// ce type est une capacité, pas un observateur. C'est la fonctionnalité
/// propriétaire qui décide de l'exercer, et seulement lorsqu'elle est active.
///
/// Les appels doivent être faits depuis le thread d'interface : le presse-papier
/// Windows est une ressource partagée, et le message qui signale son changement
/// y est justement délivré.
/// </summary>
public static partial class ClipboardAccess
{
    private const uint CfUnicodeText = 13;

    /// <summary>
    /// Lit le texte du presse-papier, ou <c>false</c> si le presse-papier ne
    /// contient pas de texte.
    /// </summary>
    public static bool TryReadText(out string text) => ReadText(out text);

    /// <summary>
    /// Lit le texte du presse-papier pour un historique : rien n'est lu si
    /// l'application qui a copié a demandé à ne pas être enregistrée — c'est ce
    /// que font Bitwarden, 1Password, KeePass et l'historique de Windows lui-même
    /// pour les mots de passe.
    /// </summary>
    /// <param name="text">Texte lu, vide s'il n'y en a pas ou s'il est exclu.</param>
    /// <param name="excluded">Vrai si le contenu porte une marque « ne pas enregistrer ».</param>
    public static bool TryReadTextForHistory(out string text, out bool excluded)
    {
        excluded = false;
        text = string.Empty;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (IsExcludedFromHistory())
            {
                excluded = true;
                return false;
            }
        }
        finally
        {
            CloseClipboard();
        }

        return ReadText(out text);
    }

    /// <summary>
    /// Les marques posées par les gestionnaires de mots de passe. Leur seule
    /// présence exclut le contenu, sauf <c>CanIncludeInClipboardHistory</c>, un
    /// DWORD qui l'exclut s'il vaut 0. À appeler presse-papier ouvert.
    /// </summary>
    private static bool IsExcludedFromHistory()
    {
        foreach (string marker in PresenceMarkers)
        {
            uint format = RegisterClipboardFormat(marker);

            if (format != 0 && IsClipboardFormatAvailable(format))
            {
                return true;
            }
        }

        uint canInclude = RegisterClipboardFormat("CanIncludeInClipboardHistory");

        if (canInclude == 0 || !IsClipboardFormatAvailable(canInclude))
        {
            return false;
        }

        IntPtr handle = GetClipboardData(canInclude);

        if (handle == IntPtr.Zero)
        {
            // Illisible : dans le doute, on n'enregistre pas.
            return true;
        }

        IntPtr pointer = GlobalLock(handle);

        if (pointer == IntPtr.Zero)
        {
            return true;
        }

        try
        {
            return Marshal.ReadInt32(pointer) == 0;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static readonly string[] PresenceMarkers =
    [
        "ExcludeClipboardContentFromMonitorProcessing",
        "Clipboard Viewer Ignore",
        "ClipboardViewerIgnore"
    ];

    private static bool ReadText(out string text)
    {
        text = string.Empty;

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            IntPtr handle = GetClipboardData(CfUnicodeText);

            if (handle == IntPtr.Zero)
            {
                return false;
            }

            IntPtr pointer = GlobalLock(handle);

            if (pointer == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                string? value = Marshal.PtrToStringUni(pointer);

                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                text = value;
                return true;
            }
            finally
            {
                GlobalUnlock(handle);
            }
        }
        finally
        {
            CloseClipboard();
        }
    }

    /// <summary>Place un texte dans le presse-papier.</summary>
    public static bool SetText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (!OpenClipboard(IntPtr.Zero))
        {
            return false;
        }

        try
        {
            if (!EmptyClipboard())
            {
                return false;
            }

            int bytes = (text.Length + 1) * sizeof(char);

            IntPtr memory = GlobalAlloc(GmemMoveable, (UIntPtr)bytes);

            if (memory == IntPtr.Zero)
            {
                return false;
            }

            IntPtr target = GlobalLock(memory);

            if (target == IntPtr.Zero)
            {
                GlobalFree(memory);
                return false;
            }

            try
            {
                Marshal.Copy(text.ToCharArray(), 0, target, text.Length);

                // Le bloc doit être terminé par un zéro : sans lui, les
                // applications lectrices liraient au-delà du texte.
                Marshal.WriteInt16(target, text.Length * sizeof(char), 0);
            }
            finally
            {
                GlobalUnlock(memory);
            }

            if (SetClipboardData(CfUnicodeText, memory) == IntPtr.Zero)
            {
                GlobalFree(memory);
                return false;
            }

            // Après SetClipboardData, la propriété du bloc appartient au système :
            // il ne doit surtout pas être libéré ici.
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private const uint GmemMoveable = 0x0002;

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenClipboard(IntPtr hWndNewOwner);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EmptyClipboard();

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr GetClipboardData(uint format);

    [LibraryImport("user32.dll", EntryPoint = "RegisterClipboardFormatW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial uint RegisterClipboardFormat(string name);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsClipboardFormatAvailable(uint format);

    [LibraryImport("user32.dll", SetLastError = true)]
    private static partial IntPtr SetClipboardData(uint format, IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalAlloc(uint flags, UIntPtr bytes);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalLock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GlobalUnlock(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GlobalFree(IntPtr handle);
}
