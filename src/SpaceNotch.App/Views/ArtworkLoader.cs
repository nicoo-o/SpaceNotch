using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace SpaceNotch_App.Views;

/// <summary>
/// Décodage des pochettes pour l'affichage.
///
/// Le décodage est mémoïsé par référence de tableau. C'est essentiel et non
/// cosmétique : la scène média est reprojetée à chaque battement de la
/// progression de lecture, parfois plusieurs fois par seconde. Sans cette
/// mémoïsation, la pochette serait redécodée à chaque battement, ce qui
/// contredirait directement la règle « aucune tâche inutile ».
/// </summary>
internal static class ArtworkLoader
{
    /// <summary>
    /// Taille de décodage, en pixels (phase D). La plus grande pochette fait
    /// environ 96 DIP ; à 200 %, 192 pixels suffisent. Une pochette de lecteur
    /// arrive souvent en 600 ou 1 000 pixels : décodée telle quelle, elle
    /// pesait plusieurs mégaoctets de mémoire vidéo pour rien.
    /// </summary>
    public const int DecodePixels = 192;

    /// <summary>
    /// Pochettes gardées : la musique, le logo d'une notification, la carte —
    /// plusieurs sont à l'écran tour à tour, une seule entrée les faisait se
    /// redécoder sans cesse.
    /// </summary>
    private const int Capacity = 4;

    private static readonly System.Collections.Generic.List<(byte[] Bytes, ImageSource? Image)> Cache = [];

    /// <summary>
    /// Retourne une source d'image pour les octets fournis, ou <c>null</c> si la
    /// pochette est absente ou illisible.
    /// </summary>
    public static async Task<ImageSource?> LoadAsync(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0)
        {
            return null;
        }

        int index = Cache.FindIndex(e => ReferenceEquals(e.Bytes, bytes));

        if (index >= 0)
        {
            (byte[] Bytes, ImageSource? Image) hit = Cache[index];
            Cache.RemoveAt(index);
            Cache.Insert(0, hit);
            return hit.Image;
        }

        ImageSource? image = null;

        try
        {
            using var stream = new InMemoryRandomAccessStream();

            IBuffer buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            await stream.WriteAsync(buffer);
            stream.Seek(0);

            var bitmap = new BitmapImage { DecodePixelWidth = DecodePixels, DecodePixelType = DecodePixelType.Physical };
            await bitmap.SetSourceAsync(stream);
            image = bitmap;
        }
        catch (Exception)
        {
            // Une pochette que le décodeur refuse ne doit pas empêcher l'affichage
            // du reste de la scène.
            image = null;
        }

        Cache.Insert(0, (bytes, image));

        if (Cache.Count > Capacity)
        {
            Cache.RemoveAt(Cache.Count - 1);
        }

        return image;
    }
}
