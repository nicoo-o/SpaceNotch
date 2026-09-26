using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>
/// Les objets du Shell dont la recherche a besoin : le dossier virtuel
/// « Applications » (<c>shell:AppsFolder</c>), qui liste les applications de
/// bureau <em>et</em> du Store, et la fabrique d'icônes des éléments. Interfaces
/// générées à la compilation, comme <see cref="Setup.ShellLink"/>.
/// </summary>
internal static partial class ShellItems
{
    public static readonly Guid FolderIdAppsFolder = new("1E87508D-89C2-42F0-8A7E-645A0F50CA58");
    public static readonly Guid BhidEnumItems = new("94F60519-2850-4924-AA5A-D15E84868039");
    public static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    public static readonly Guid IidEnumShellItems = new("70629033-E363-4A28-A567-0DB78006E6D7");
    public static readonly Guid IidShellItemImageFactory = new("BCC18B79-BA16-442F-80C4-8A59C30C463B");

    public const uint SigdnNormalDisplay = 0x00000000;
    public const uint SigdnParentRelativeParsing = 0x80018001;

    public const int SiigbfBiggerSizeOk = 0x1;
    public const int SiigbfIconOnly = 0x4;

    private static readonly StrategyBasedComWrappers Wrappers = new();

    [LibraryImport("shell32.dll")]
    public static partial int SHGetKnownFolderItem(in Guid rfid, uint flags, IntPtr token, in Guid riid, out IntPtr ppv);

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    public static partial int SHCreateItemFromParsingName(string path, IntPtr bindContext, in Guid riid, out IntPtr ppv);

    /// <summary>Enveloppe un pointeur COM dans son interface générée, et relâche le pointeur brut.</summary>
    public static T Wrap<T>(IntPtr unknown)
        where T : class
    {
        try
        {
            return (T)Wrappers.GetOrCreateObjectForComInstance(unknown, CreateObjectFlags.UniqueInstance);
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>Lit une chaîne allouée par le Shell, puis la libère.</summary>
    public static string? TakeString(IntPtr text)
    {
        if (text == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Marshal.PtrToStringUni(text);
        }
        finally
        {
            Marshal.FreeCoTaskMem(text);
        }
    }

    [GeneratedComInterface]
    [Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE")]
    internal partial interface IShellItem
    {
        void BindToHandler(IntPtr bindContext, in Guid handler, in Guid riid, out IntPtr ppv);

        void GetParent(out IntPtr parent);

        void GetDisplayName(uint sigdnName, out IntPtr name);

        void GetAttributes(uint mask, out uint attributes);

        void Compare(IntPtr other, uint hint, out int order);
    }

    [GeneratedComInterface]
    [Guid("70629033-E363-4A28-A567-0DB78006E6D7")]
    internal partial interface IEnumShellItems
    {
        [PreserveSig]
        int Next(uint count, out IntPtr item, out uint fetched);

        void Skip(uint count);

        void Reset();

        void Clone(out IntPtr copy);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeSize
    {
        public int Width;
        public int Height;
    }

    [GeneratedComInterface]
    [Guid("BCC18B79-BA16-442F-80C4-8A59C30C463B")]
    internal partial interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(NativeSize size, int flags, out IntPtr bitmap);
    }
}
