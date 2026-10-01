using System;
using System.Runtime.InteropServices;

namespace SpaceNotch.Platform.Windows.Capture;

/// <summary>Une image d'écran brute : BGRA 8 bits, lignes de haut en bas.</summary>
/// <param name="X">Bord gauche, en pixels physiques du bureau virtuel.</param>
/// <param name="Y">Bord haut.</param>
public sealed record ScreenImage(int X, int Y, int Width, int Height, byte[] Pixels)
{
    /// <summary>Découpe un rectangle de l'image (coordonnées relatives à l'image).</summary>
    public ScreenImage? Crop(int x, int y, int width, int height)
    {
        x = Math.Clamp(x, 0, Width);
        y = Math.Clamp(y, 0, Height);
        width = Math.Min(width, Width - x);
        height = Math.Min(height, Height - y);

        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var pixels = new byte[width * height * 4];

        for (int row = 0; row < height; row++)
        {
            Buffer.BlockCopy(Pixels, (((y + row) * Width) + x) * 4, pixels, row * width * 4, width * 4);
        }

        return new ScreenImage(X + x, Y + y, width, height, pixels);
    }
}

/// <summary>
/// Capture d'écran pour la capture de texte (W4) : une copie GDI du moniteur,
/// prise une fois, que la fenêtre de sélection fige à l'écran. Rien n'est
/// écrit sur le disque ; l'image vit le temps de la sélection.
/// </summary>
public static partial class ScreenGrab
{
    private const int SRCCOPY = 0x00CC0020;
    private const int CAPTUREBLT = 0x40000000;

    /// <summary>Copie un rectangle du bureau ; <c>null</c> en cas d'échec.</summary>
    public static ScreenImage? Capture(int x, int y, int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        IntPtr screen = GetDC(IntPtr.Zero);

        if (screen == IntPtr.Zero)
        {
            return null;
        }

        IntPtr memory = CreateCompatibleDC(screen);
        IntPtr bitmap = CreateCompatibleBitmap(screen, width, height);
        IntPtr old = SelectObject(memory, bitmap);

        try
        {
            if (!BitBlt(memory, 0, 0, width, height, screen, x, y, SRCCOPY | CAPTUREBLT))
            {
                return null;
            }

            var info = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0
            };

            var pixels = new byte[width * height * 4];
            SelectObject(memory, old);

            unsafe
            {
                fixed (byte* p = pixels)
                {
                    if (GetDIBits(memory, bitmap, 0, (uint)height, p, ref info, 0) == 0)
                    {
                        return null;
                    }
                }
            }

            // GDI laisse l'alpha à zéro : l'image est opaque.
            for (int i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = 0xFF;
            }

            return new ScreenImage(x, y, width, height, pixels);
        }
        finally
        {
            SelectObject(memory, old);
            DeleteObject(bitmap);
            DeleteDC(memory);
            _ = ReleaseDC(IntPtr.Zero, screen);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize;
        public int biWidth;
        public int biHeight;
        public short biPlanes;
        public short biBitCount;
        public int biCompression;
        public int biSizeImage;
        public int biXPelsPerMeter;
        public int biYPelsPerMeter;
        public int biClrUsed;
        public int biClrImportant;
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleBitmap(IntPtr dc, int width, int height);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BitBlt(IntPtr dest, int x, int y, int width, int height, IntPtr source, int sx, int sy, int rop);

    [LibraryImport("gdi32.dll")]
    private static unsafe partial int GetDIBits(IntPtr dc, IntPtr bitmap, uint start, uint lines, byte* bits, ref BITMAPINFOHEADER info, uint usage);
}
