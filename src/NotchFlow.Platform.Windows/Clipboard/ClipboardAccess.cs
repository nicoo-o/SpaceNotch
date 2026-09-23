using System;
using System.Runtime.InteropServices;
using System.Text;

namespace NotchFlow.Platform.Windows.Clipboard;

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
    public static bool TryReadText(out string text)
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
