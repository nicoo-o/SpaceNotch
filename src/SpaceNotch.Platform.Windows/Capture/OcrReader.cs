using System;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using SpaceNotch.Core.Capture;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace SpaceNotch.Platform.Windows.Capture;

/// <summary>
/// Reconnaissance de texte par l'OCR intégré à Windows (Windows.Media.Ocr) :
/// hors ligne, dans les langues installées sur le poste. Aucun service en ligne.
/// </summary>
public static class OcrReader
{
    /// <summary>Vrai si Windows sait lire au moins une langue du profil.</summary>
    public static bool IsAvailable => OcrEngine.AvailableRecognizerLanguages.Count > 0;

    /// <summary>Le texte d'une image, mis en forme par <see cref="OcrText.Join"/> ; vide si rien n'est lu.</summary>
    public static async Task<string> ReadAsync(ScreenImage image)
    {
        ArgumentNullException.ThrowIfNull(image);

        OcrEngine? engine = OcrEngine.TryCreateFromUserProfileLanguages();

        if (engine is null || image.Width > OcrEngine.MaxImageDimension || image.Height > OcrEngine.MaxImageDimension)
        {
            return string.Empty;
        }

        using SoftwareBitmap bitmap = SoftwareBitmap.CreateCopyFromBuffer(
            image.Pixels.AsBuffer(),
            BitmapPixelFormat.Bgra8,
            image.Width,
            image.Height,
            BitmapAlphaMode.Premultiplied);

        OcrResult result = await engine.RecognizeAsync(bitmap);
        return OcrText.Join(result.Lines.Select(l => l.Text));
    }
}
