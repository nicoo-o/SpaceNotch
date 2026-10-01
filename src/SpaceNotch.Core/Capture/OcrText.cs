using System.Text;

namespace SpaceNotch.Core.Capture;

/// <summary>
/// Capture de texte (W4) : on trace un rectangle sur n'importe quoi — une image,
/// une vidéo, un PDF scanné — et le texte reconnu arrive dans le presse-papier.
/// Ici, la mise en forme de ce que l'OCR de Windows renvoie, et le rectangle
/// tracé à la souris.
/// </summary>
public static class OcrText
{
    /// <summary>Côté minimal d'une sélection, en pixels : en deçà, c'est un clic.</summary>
    public const int MinimumSide = 8;

    /// <summary>
    /// Lignes reconnues → texte : espaces réduits, lignes vides retirées, fins de
    /// ligne Windows. Les mots coupés en fin de ligne (« impor- / tant ») sont recollés.
    /// </summary>
    public static string Join(IEnumerable<string> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        var kept = new List<string>();

        foreach (string raw in lines)
        {
            string line = string.Join(' ', (raw ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            if (line.Length == 0)
            {
                continue;
            }

            if (kept.Count > 0 && kept[^1].Length > 1 && kept[^1].EndsWith('-') && char.IsLetter(kept[^1][^2]) && char.IsLower(line[0]))
            {
                kept[^1] = kept[^1][..^1] + line;
                continue;
            }

            kept.Add(line);
        }

        return string.Join("\r\n", kept);
    }

    /// <summary>« 3 lignes · FACTURE n° 2026-118 … » : l'aperçu de la pastille.</summary>
    public static string Preview(string text, int length = 48)
    {
        ArgumentNullException.ThrowIfNull(text);

        string flat = text.Replace("\r\n", " · ", StringComparison.Ordinal);
        return flat.Length <= length ? flat : string.Concat(flat.AsSpan(0, length - 1), "…");
    }

    /// <summary>Nombre de lignes d'un texte joint.</summary>
    public static int LineCount(string text)
        => string.IsNullOrEmpty(text) ? 0 : text.Split("\r\n").Length;

    /// <summary>Rectangle normalisé entre deux coins, dans n'importe quel sens de tracé.</summary>
    public static (int X, int Y, int Width, int Height) Selection(int x1, int y1, int x2, int y2)
        => (Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1), Math.Abs(y2 - y1));

    /// <summary>Vrai si le rectangle est assez grand pour contenir du texte.</summary>
    public static bool IsUsable(int width, int height) => width >= MinimumSide && height >= MinimumSide;
}
