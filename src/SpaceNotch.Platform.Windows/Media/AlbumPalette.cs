using System;
using System.IO;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SpaceNotch.Platform.Windows.Media;

/// <summary>
/// Pochette décodée : ses octets, réutilisables par l'interface, et la teinte
/// dominante qu'elle suggère.
/// </summary>
public readonly record struct AlbumArtwork(byte[]? Bytes, ActivityTint? Tint);

/// <summary>
/// Analyse d'une pochette : octets encodés conservés tels quels, et couleur
/// dominante extraite des pixels.
///
/// Rien n'est coloré autour de la notch (choix E1) : la teinte vit dans le
/// contenu — la trame et l'icône — et doit donc se lire sur le noir. Elle est
/// ramenée dans un registre saturé mais jamais criard (vague 5, D2).
/// </summary>
public static class AlbumPalette
{
    /// <summary>Nombre de pixels visé lors de l'échantillonnage, par côté.</summary>
    private const int SampleSize = 24;

    /// <summary>Retourne les octets de la pochette et sa teinte dominante.</summary>
    public static async Task<AlbumArtwork> ReadAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return default;
        }

        try
        {
            using IRandomAccessStreamWithContentType stream = await reference.OpenReadAsync();

            byte[] bytes = await ReadAllBytesAsync(stream);
            ActivityTint? tint = await ExtractTintAsync(stream);

            return new AlbumArtwork(bytes, tint);
        }
        catch (Exception)
        {
            // Une pochette illisible ne doit jamais interrompre le suivi média :
            // l'Island se contente alors de sa teinte de référence.
            return default;
        }
    }

    private static async Task<byte[]> ReadAllBytesAsync(IRandomAccessStreamWithContentType stream)
    {
        stream.Seek(0);

        using var memory = new MemoryStream();
        using var reader = new DataReader(stream.GetInputStreamAt(0));

        await reader.LoadAsync((uint)stream.Size);

        byte[] buffer = new byte[stream.Size];
        reader.ReadBytes(buffer);
        memory.Write(buffer, 0, buffer.Length);

        return memory.ToArray();
    }

    private static async Task<ActivityTint?> ExtractTintAsync(IRandomAccessStreamWithContentType stream)
    {
        stream.Seek(0);

        BitmapDecoder decoder = await BitmapDecoder.CreateAsync(stream);

        // Transformation à petite échelle : sur une pochette 2000 × 2000, moyenner
        // les pixels d'origine coûterait des dizaines de milliers d'opérations pour
        // un résultat identique.
        var transform = new BitmapTransform
        {
            ScaledWidth = SampleSize,
            ScaledHeight = SampleSize,
            InterpolationMode = BitmapInterpolationMode.Fant
        };

        PixelDataProvider pixels = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);

        byte[] data = pixels.DetachPixelData();

        return ComputeDominantTint(data);
    }

    /// <summary>
    /// Couleur tirée de la pochette (D2) : teinte dominante, saturation relevée,
    /// lisible sur le noir de la notch (trame, icône). Voir <see cref="DominantColor"/>.
    /// </summary>
    private static ActivityTint? ComputeDominantTint(byte[] bgra) => DominantColor.FromBgra(bgra);
}
