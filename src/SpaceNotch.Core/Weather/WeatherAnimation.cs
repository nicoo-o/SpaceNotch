using SpaceNotch.Core.Motion;

namespace SpaceNotch.Core.Weather;

/// <summary>
/// Aperçu météo (F10) : l'icône vit — la pluie tombe vraiment, pixel par
/// pixel, la neige descend plus lentement, l'orage s'allume par éclairs.
/// Le soleil, les nuages et le brouillard restent immobiles.
/// </summary>
public static class WeatherAnimation
{
    public const int FrameMilliseconds = 160;

    private const int Size = PixelGlyphs.Size;

    /// <summary>Nombre d'images d'un cycle, 0 si l'icône est immobile.</summary>
    public static int Length(string iconKey) => iconKey switch
    {
        "WeatherRain" => 4,
        "WeatherSnow" => 8,
        "WeatherStorm" => 6,
        _ => 0
    };

    /// <summary>Image <paramref name="frame"/> de l'icône animée.</summary>
    public static bool[] Frame(string iconKey, int frame)
    {
        bool[] cloud = Cloud();

        switch (iconKey)
        {
            case "WeatherRain":
                // Trois gouttes décalées, qui descendent d'une rangée par image.
                foreach ((int column, int phase) in new[] { (1, 0), (3, 2), (5, 1) })
                {
                    int row = 3 + ((frame + phase) % 4);
                    cloud[(row * Size) + column] = true;
                }

                return cloud;

            case "WeatherSnow":
                foreach ((int column, int phase) in new[] { (0, 0), (2, 4), (4, 2), (6, 6) })
                {
                    int row = 3 + (((frame + phase) / 2) % 4);
                    cloud[(row * Size) + column] = true;
                }

                return cloud;

            case "WeatherStorm":
                // L'éclair n'apparaît que deux images sur six.
                return frame % 6 is 1 or 2 ? (bool[])((PixelGlyphs.Resolve("WeatherStorm") as bool[])!).Clone() : cloud;

            default:
                return (bool[])((PixelGlyphs.Resolve(iconKey) as bool[]) ?? new bool[Size * Size]).Clone();
        }
    }

    private static bool[] Cloud()
    {
        var mask = new bool[Size * Size];
        string[] rows = ["..xx...", ".xxxxx.", "xxxxxxx"];

        for (int r = 0; r < rows.Length; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                mask[(r * Size) + c] = rows[r][c] == 'x';
            }
        }

        return mask;
    }
}
