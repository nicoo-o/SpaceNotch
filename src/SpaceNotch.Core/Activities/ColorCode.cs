using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Couleur copiée (F5) : reconnaît un code couleur dans le presse-papier —
/// <c>#7FE6FF</c>, <c>#7fe</c>, <c>rgb(127, 230, 255)</c>, <c>hsl(193, 100%, 75%)</c> —
/// et le réécrit dans les trois formats, prêts à recopier.
/// </summary>
public readonly partial record struct ColorCode(byte R, byte G, byte B)
{
    /// <summary>Le texte copié est-il une couleur, et laquelle ? Rien d'autre qu'une couleur, espaces mis à part.</summary>
    public static bool TryParse(string? text, out ColorCode color)
    {
        color = default;

        if (string.IsNullOrWhiteSpace(text) || text.Length > 40)
        {
            return false;
        }

        string s = text.Trim();

        Match hex = HexPattern().Match(s);

        if (hex.Success)
        {
            string h = hex.Groups[1].Value;

            if (h.Length == 3)
            {
                h = string.Concat(h[0], h[0], h[1], h[1], h[2], h[2]);
            }

            color = new ColorCode(
                byte.Parse(h.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(h.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                byte.Parse(h.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
            return true;
        }

        Match rgb = RgbPattern().Match(s);

        if (rgb.Success)
        {
            int r = int.Parse(rgb.Groups[1].Value, CultureInfo.InvariantCulture);
            int g = int.Parse(rgb.Groups[2].Value, CultureInfo.InvariantCulture);
            int b = int.Parse(rgb.Groups[3].Value, CultureInfo.InvariantCulture);

            if (r > 255 || g > 255 || b > 255)
            {
                return false;
            }

            color = new ColorCode((byte)r, (byte)g, (byte)b);
            return true;
        }

        Match hsl = HslPattern().Match(s);

        if (hsl.Success)
        {
            double h = double.Parse(hsl.Groups[1].Value, CultureInfo.InvariantCulture);
            double sat = double.Parse(hsl.Groups[2].Value, CultureInfo.InvariantCulture) / 100;
            double l = double.Parse(hsl.Groups[3].Value, CultureInfo.InvariantCulture) / 100;

            if (h > 360 || sat > 1 || l > 1)
            {
                return false;
            }

            color = FromHsl(h, sat, l);
            return true;
        }

        return false;
    }

    /// <summary>« #7FE6FF ».</summary>
    public string Hex => string.Create(CultureInfo.InvariantCulture, $"#{R:X2}{G:X2}{B:X2}");

    /// <summary>« rgb(127, 230, 255) ».</summary>
    public string Rgb => string.Create(CultureInfo.InvariantCulture, $"rgb({R}, {G}, {B})");

    /// <summary>« hsl(193, 100%, 75%) », valeurs arrondies.</summary>
    public string Hsl
    {
        get
        {
            (double h, double s, double l) = ToHsl();
            return string.Create(CultureInfo.InvariantCulture, $"hsl({Math.Round(h)}, {Math.Round(s * 100)}%, {Math.Round(l * 100)}%)");
        }
    }

    /// <summary>Texte lisible posé sur cette couleur : noir sur clair, blanc sur sombre.</summary>
    public bool IsLight => ((0.299 * R) + (0.587 * G) + (0.114 * B)) > 150;

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        double l = (max + min) / 2, d = max - min;

        if (d < 1e-9)
        {
            return (0, 0, l);
        }

        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? ((g - b) / d) + (g < b ? 6 : 0) : max == g ? ((b - r) / d) + 2 : ((r - g) / d) + 4;

        return (h * 60, s, l);
    }

    public static ColorCode FromHsl(double h, double s, double l)
    {
        double c = (1 - Math.Abs((2 * l) - 1)) * s;
        double x = c * (1 - Math.Abs(((h / 60) % 2) - 1));
        double m = l - (c / 2);
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x)
        };

        static byte To(double v) => (byte)Math.Clamp(Math.Round(v * 255), 0, 255);
        return new ColorCode(To(r + m), To(g + m), To(b + m));
    }

    [GeneratedRegex(@"^#([0-9a-fA-F]{6}|[0-9a-fA-F]{3})$", RegexOptions.CultureInvariant)]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"^rgba?\(\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*[, ]\s*(\d{1,3})\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex RgbPattern();

    [GeneratedRegex(@"^hsla?\(\s*(\d{1,3}(?:\.\d+)?)(?:deg)?\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*[, ]\s*(\d{1,3}(?:\.\d+)?)%\s*(?:[,/]\s*[\d.]+%?\s*)?\)$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex HslPattern();
}
