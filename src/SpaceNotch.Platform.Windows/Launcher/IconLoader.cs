using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Launcher;

/// <summary>Une icône en pixels BGRA prémultipliés, de haut en bas.</summary>
public sealed record IconPixels(int Width, int Height, byte[] Bgra);

/// <summary>
/// La vraie icône d'une application ou d'un fichier, telle que l'Explorateur
/// la montre : <c>IShellItemImageFactory</c>, qui sait aussi bien les
/// applications du Store que les exécutables et les documents. À appeler sur
/// un fil STA.
/// </summary>
public static partial class IconLoader
{
    public static IconPixels? Load(string path, int size)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        Guid iid = ShellItems.IidShellItemImageFactory;

        if (ShellItems.SHCreateItemFromParsingName(path, IntPtr.Zero, in iid, out IntPtr factoryPtr) != 0 || factoryPtr == IntPtr.Zero)
        {
            return null;
        }

        var factory = ShellItems.Wrap<ShellItems.IShellItemImageFactory>(factoryPtr);
        var requested = new ShellItems.NativeSize { Width = size, Height = size };

        if (factory.GetImage(requested, ShellItems.SiigbfIconOnly | ShellItems.SiigbfBiggerSizeOk, out IntPtr bitmap) != 0 || bitmap == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return Read(bitmap);
        }
        finally
        {
            DeleteObject(bitmap);
        }
    }

    private static IconPixels? Read(IntPtr bitmap)
    {
        var info = default(BitmapInfo);

        if (GetObject(bitmap, Marshal.SizeOf<BitmapInfo>(), ref info) == 0 || info.Width <= 0 || info.Height == 0)
        {
            return null;
        }

        int width = info.Width;
        int height = Math.Abs(info.Height);

        var header = new BitmapInfoHeader
        {
            Size = Marshal.SizeOf<BitmapInfoHeader>(),
            Width = width,
            Height = -height, // de haut en bas
            Planes = 1,
            BitCount = 32,
            Compression = 0
        };

        byte[] pixels = new byte[width * height * 4];
        IntPtr screen = GetDC(IntPtr.Zero);

        try
        {
            if (GetDIBits(screen, bitmap, 0, (uint)height, pixels, ref header, 0) == 0)
            {
                return null;
            }
        }
        finally
        {
            _ = ReleaseDC(IntPtr.Zero, screen);
        }

        return new IconPixels(width, height, pixels);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Type;
        public int Width;
        public int Height;
        public int WidthBytes;
        public ushort Planes;
        public ushort BitsPixel;
        public IntPtr Bits;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [LibraryImport("gdi32.dll", EntryPoint = "GetObjectW")]
    private static partial int GetObject(IntPtr handle, int size, ref BitmapInfo info);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, [Out] byte[] bits, ref BitmapInfoHeader info, uint usage);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr handle);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr window);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr window, IntPtr dc);
}
