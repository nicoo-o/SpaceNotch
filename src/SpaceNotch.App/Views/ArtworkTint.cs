using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using Windows.Graphics.Imaging;

namespace SpaceNotch_App.Views;

/// <summary>
/// Couleur tirée du logo de l'application qui notifie (D2) : décodée une fois
/// par image, en 16 × 16, puis gardée. Discord colore la notch en violet,
/// WhatsApp en vert, sans que la fonctionnalité ait à le savoir.
/// </summary>
internal static class ArtworkTint
{
    private static readonly Dictionary<int, ActivityTint?> Cache = [];

    /// <summary>Teinte déjà connue pour ces octets ; sinon lance le calcul et rappelle <paramref name="ready"/>.</summary>
    public static ActivityTint? Get(byte[]? artwork, Action ready)
    {
        ArgumentNullException.ThrowIfNull(ready);

        if (artwork is not { Length: > 0 })
        {
            return null;
        }

        int key = Key(artwork);

        lock (Cache)
        {
            if (Cache.TryGetValue(key, out ActivityTint? known))
            {
                return known;
            }

            Cache[key] = null;
        }

        _ = ComputeAsync(artwork, key, ready);
        return null;
    }

    private static async Task ComputeAsync(byte[] artwork, int key, Action ready)
    {
        ActivityTint? tint = null;

        try
        {
            using var memory = new MemoryStream(artwork);
            using global::Windows.Storage.Streams.IRandomAccessStream stream = memory.AsRandomAccessStream();
            BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);
            PixelDataProvider pixels = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Straight,
                new BitmapTransform { ScaledWidth = 16, ScaledHeight = 16, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            tint = DominantColor.FromBgra(pixels.DetachPixelData());
        }
        catch (Exception)
        {
            // Un logo illisible garde la teinte de l'état : rien à signaler.
        }

        lock (Cache)
        {
            Cache[key] = tint;
        }

        if (tint is not null)
        {
            ready();
        }
    }

    private static int Key(byte[] bytes)
    {
        var hash = new HashCode();
        hash.Add(bytes.Length);

        for (int i = 0; i < bytes.Length; i += Math.Max(1, bytes.Length / 64))
        {
            hash.Add(bytes[i]);
        }

        return hash.ToHashCode();
    }
}
