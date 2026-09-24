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
/// La couleur est fortement désaturée avant d'être retournée. L'Island n'a pas à
/// afficher la pochette : elle n'en garde qu'une suggestion, et une teinte vive
/// produirait exactement l'effet « gradient RVB » que le projet refuse.
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
    /// Moyenne pondérée par la saturation : un aplat gris ne doit pas tirer la
    /// teinte vers le gris, alors que la zone colorée, elle, doit compter.
    /// </summary>
    private static ActivityTint? ComputeDominantTint(byte[] bgra)
    {
        const int BytesPerPixel = 4;

        double weightSum = 0;
        double redSum = 0;
        double greenSum = 0;
        double blueSum = 0;

        for (int i = 0; i + (BytesPerPixel - 1) < bgra.Length; i += BytesPerPixel)
        {
            byte b = bgra[i];
            byte g = bgra[i + 1];
            byte r = bgra[i + 2];

            int max = Math.Max(r, Math.Max(g, b));
            int min = Math.Min(r, Math.Min(g, b));

            // Les pixels presque noirs ou presque blancs n'apportent pas de
            // couleur : les inclure reviendrait à moyenner du vide.
            if (max < 24 || min > 232)
            {
                continue;
            }

            double weight = max - min;

            if (weight <= 0)
            {
                continue;
            }

            redSum += r * weight;
            greenSum += g * weight;
            blueSum += b * weight;
            weightSum += weight;
        }

        if (weightSum <= 0)
        {
            return null;
        }

        byte cr = (byte)Math.Clamp(redSum / weightSum, 0, 255);
        byte cg = (byte)Math.Clamp(greenSum / weightSum, 0, 255);
        byte cb = (byte)Math.Clamp(blueSum / weightSum, 0, 255);

        return Desaturate(cr, cg, cb);
    }

    /// <summary>
    /// Ramène la couleur vers un registre discret : saturation réduite, luminosité
    /// recentrée. C'est ce qui transforme une pochette criarde en suggestion.
    /// </summary>
    private static ActivityTint Desaturate(byte r, byte g, byte b)
    {
        const double SaturationRetention = 0.34;
        const double TargetLuminance = 0.42;

        (double hue, double saturation, double _) = ToHsl(r, g, b);

        double keptSaturation = saturation * SaturationRetention;

        (byte nr, byte ng, byte nb) = FromHsl(hue, keptSaturation, TargetLuminance);

        return new ActivityTint(nr, ng, nb);
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(byte r, byte g, byte b)
    {
        double rd = r / 255.0;
        double gd = g / 255.0;
        double bd = b / 255.0;

        double max = Math.Max(rd, Math.Max(gd, bd));
        double min = Math.Min(rd, Math.Min(gd, bd));
        double delta = max - min;

        double lightness = (max + min) / 2.0;
        double saturation = delta <= 0 ? 0 : delta / (1 - Math.Abs((2 * lightness) - 1));

        double hue = 0;

        if (delta > 0)
        {
            if (max == rd)
            {
                hue = 60 * (((gd - bd) / delta) % 6);
            }
            else if (max == gd)
            {
                hue = 60 * (((bd - rd) / delta) + 2);
            }
            else
            {
                hue = 60 * (((rd - gd) / delta) + 4);
            }
        }

        if (hue < 0)
        {
            hue += 360;
        }

        return (hue, saturation, lightness);
    }

    private static (byte R, byte G, byte B) FromHsl(double hue, double saturation, double lightness)
    {
        double c = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        double h = hue / 60.0;
        double x = c * (1 - Math.Abs((h % 2) - 1));

        (double r, double g, double b) = h switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x)
        };

        double m = lightness - (c / 2.0);

        return (
            (byte)Math.Clamp((r + m) * 255, 0, 255),
            (byte)Math.Clamp((g + m) * 255, 0, 255),
            (byte)Math.Clamp((b + m) * 255, 0, 255));
    }
}
