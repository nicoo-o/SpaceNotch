using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>
/// Raccourcis <c>.lnk</c>, par l'objet ShellLink de Windows — celui qu'utilise
/// l'Explorateur. Interfaces COM générées à la compilation
/// (<c>GeneratedComInterface</c>) : aucune dépendance au COM par réflexion,
/// que l'élagage de l'application désactive.
/// </summary>
public static partial class ShellLink
{
    private static readonly Guid ClsidShellLink = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid IidIUnknown = new("00000000-0000-0000-C000-000000000046");
    private const uint ClsctxInprocServer = 1;

    /// <summary>Crée ou remplace un raccourci.</summary>
    /// <param name="shortcutPath">Fichier <c>.lnk</c> à écrire.</param>
    /// <param name="targetPath">Exécutable visé.</param>
    /// <param name="description">Info-bulle et nom lu par le Narrateur.</param>
    public static void Create(string shortcutPath, string targetPath, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shortcutPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);

        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);

        Guid clsid = ClsidShellLink;
        Guid iid = IidIUnknown;
        Marshal.ThrowExceptionForHR(CoCreateInstance(in clsid, IntPtr.Zero, ClsctxInprocServer, in iid, out IntPtr unknown));

        try
        {
            var wrappers = new StrategyBasedComWrappers();
            object instance = wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);

            var link = (IShellLinkW)instance;
            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);
            link.SetDescription(description);
            link.SetIconLocation(targetPath, 0);

            var file = (IPersistFile)instance;
            file.Save(shortcutPath, fRemember: true);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Cible d'un raccourci existant, ou <c>null</c> s'il est illisible ou ne
    /// vise pas un fichier (un élément virtuel, une page web).
    /// </summary>
    public static string? ResolveTarget(string shortcutPath)
    {
        if (string.IsNullOrWhiteSpace(shortcutPath))
        {
            return null;
        }

        Guid clsid = ClsidShellLink;
        Guid iid = IidIUnknown;

        if (CoCreateInstance(in clsid, IntPtr.Zero, ClsctxInprocServer, in iid, out IntPtr unknown) != 0)
        {
            return null;
        }

        const int Capacity = 1024;
        IntPtr buffer = Marshal.AllocHGlobal(Capacity * sizeof(char));

        try
        {
            var wrappers = new StrategyBasedComWrappers();
            object instance = wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);

            ((IPersistFile)instance).Load(shortcutPath, 0);
            ((IShellLinkW)instance).GetPath(buffer, Capacity, IntPtr.Zero, 0);

            string? target = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrWhiteSpace(target) ? null : target;
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            Marshal.Release(unknown);
        }
    }

    /// <summary>Supprime un raccourci s'il existe.</summary>
    public static void Delete(string shortcutPath)
    {
        if (!string.IsNullOrWhiteSpace(shortcutPath) && File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(
        in Guid rclsid,
        IntPtr pUnkOuter,
        uint dwClsContext,
        in Guid riid,
        out IntPtr ppv);

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);

        void GetIDList(out IntPtr ppidl);

        void SetIDList(IntPtr pidl);

        void GetDescription(IntPtr pszName, int cch);

        void SetDescription(string pszName);

        void GetWorkingDirectory(IntPtr pszDir, int cch);

        void SetWorkingDirectory(string pszDir);

        void GetArguments(IntPtr pszArgs, int cch);

        void SetArguments(string pszArgs);

        void GetHotkey(out short pwHotkey);

        void SetHotkey(short wHotkey);

        void GetShowCmd(out int piShowCmd);

        void SetShowCmd(int iShowCmd);

        void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);

        void SetIconLocation(string pszIconPath, int iIcon);

        void SetRelativePath(string pszPathRel, uint dwReserved);

        void Resolve(IntPtr hwnd, uint fFlags);

        void SetPath(string pszFile);
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid pClassID);

        [PreserveSig]
        int IsDirty();

        void Load(string pszFileName, uint dwMode);

        void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);

        void SaveCompleted(string pszFileName);

        void GetCurFile(out IntPtr ppszFileName);
    }
}
