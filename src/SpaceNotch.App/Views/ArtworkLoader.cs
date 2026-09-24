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
    private static byte[]? _cachedBytes;
    private static ImageSource? _cachedImage;

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

        if (ReferenceEquals(bytes, _cachedBytes))
        {
            return _cachedImage;
        }

        try
        {
            using var stream = new InMemoryRandomAccessStream();

            IBuffer buffer = CryptographicBuffer.CreateFromByteArray(bytes);
            await stream.WriteAsync(buffer);
            stream.Seek(0);

            var image = new BitmapImage();
            await image.SetSourceAsync(stream);

            _cachedBytes = bytes;
            _cachedImage = image;

            return image;
        }
        catch (Exception)
        {
            // Une pochette que le décodeur refuse ne doit pas empêcher l'affichage
            // du reste de la scène.
            _cachedBytes = bytes;
            _cachedImage = null;

            return null;
        }
    }
}
