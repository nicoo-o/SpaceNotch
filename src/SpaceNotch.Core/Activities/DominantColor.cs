using System;

namespace SpaceNotch.Core.Activities;

/// <summary>
/// Couleur tirée de la pochette ou du logo (D2) : moyenne des pixels pondérée
/// par leur saturation, puis saturation relevée et luminosité recentrée, pour
/// une teinte qui se lit sur le noir de la notch (trame, icône).
/// </summary>
public static class DominantColor
{
    /// <summary>
    /// Teinte dominante d'une image BGRA (4 octets par pixel), ou <c>null</c>
    /// si l'image n'a pas de couleur (noir et blanc, gris).
    /// </summary>
    public static ActivityTint? FromBgra(ReadOnlySpan<byte> bgra)
    {
        double weightSum = 0, red = 0, green = 0, blue = 0;

        for (int i = 0; i + 3 < bgra.Length; i += 4)
        {
            byte b = bgra[i], g = bgra[i + 1], r = bgra[i + 2], a = bgra[i + 3];
            int max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));

            // Transparent, presque noir, presque blanc : aucune couleur à en tirer.
            if (a < 128 || max < 24 || min > 232)
            {
                continue;
            }

            double weight = (max - min) * (max - min);

            if (weight <= 0)
            {
                continue;
            }

            red += r * weight;
            green += g * weight;
            blue += b * weight;
            weightSum += weight;
        }

        // Moins d'un pixel franchement coloré sur l'ensemble : l'image est grise.
        if (weightSum < 24 * 24)
        {
            return null;
        }

        var average = new ColorCode(
            (byte)Math.Clamp(red / weightSum, 0, 255),
            (byte)Math.Clamp(green / weightSum, 0, 255),
            (byte)Math.Clamp(blue / weightSum, 0, 255));

        return ForBlack(average);
    }

    /// <summary>
    /// Ramène une couleur dans le registre de la notch : saturation au moins
    /// 0,55, luminosité entre 0,58 et 0,72. Une pochette terne donne quand même
    /// une teinte lisible ; une pochette criarde ne brûle pas l'écran.
    /// </summary>
    public static ActivityTint ForBlack(ColorCode color)
    {
        (double h, double s, double l) = color.ToHsl();
        ColorCode adjusted = ColorCode.FromHsl(h, Math.Clamp(Math.Max(s, 0.55), 0, 0.85), Math.Clamp(l, 0.58, 0.72));
        return new ActivityTint(adjusted.R, adjusted.G, adjusted.B);
    }
}
