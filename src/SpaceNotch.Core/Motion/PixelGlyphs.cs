using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Les icônes de la notch, dessinées dans sa propre matière : une grille de
/// 7 × 7 pixels, comme la grille hypnotique et le logo.
///
/// <para>
/// Les pictogrammes de Windows — une cloche, un haut-parleur, le logo
/// Bluetooth — venaient d'un autre monde que la notch et cassaient
/// l'immersion. Ici, chaque icône est un motif de pixels allumés ; les pixels
/// éteints restent visibles en filigrane, et à l'apparition les pixels
/// s'allument du centre vers les bords (<see cref="LightOrder"/>).
/// </para>
///
/// <para>
/// Une clé inconnue — un greffon, un caractère isolé — n'a pas de motif :
/// l'appelant garde alors son glyphe de police.
/// </para>
/// </summary>
public static class PixelGlyphs
{
    /// <summary>Côté de la grille, en pixels.</summary>
    public const int Size = 7;

    private static readonly Dictionary<string, bool[]> Glyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Music"] = Mask(
            "..xxxxx",
            "..x...x",
            "..x...x",
            "..x...x",
            "xxx.xxx",
            "xxx.xxx",
            "......."),
        ["Volume"] = Mask(
            "..x...x",
            ".xx.x.x",
            "xxx.x.x",
            "xxx.x.x",
            "xxx.x.x",
            ".xx.x.x",
            "..x...x"),
        ["VolumeHigh"] = Mask(
            "..x...x",
            ".xx.x.x",
            "xxx.x.x",
            "xxx.x.x",
            "xxx.x.x",
            ".xx.x.x",
            "..x...x"),
        ["VolumeMedium"] = Mask(
            "..x....",
            ".xx....",
            "xxx.x..",
            "xxx.x..",
            "xxx.x..",
            ".xx....",
            "..x...."),
        ["VolumeLow"] = Mask(
            "..x....",
            ".xx....",
            "xxx....",
            "xxx.x..",
            "xxx....",
            ".xx....",
            "..x...."),
        ["VolumeMute"] = Mask(
            "..x....",
            ".xx....",
            "xxx.x.x",
            "xxx..x.",
            "xxx.x.x",
            ".xx....",
            "..x...."),
        ["Brightness"] = Mask(
            "x..x..x",
            ".x...x.",
            "..xxx..",
            "x.xxx.x",
            "..xxx..",
            ".x...x.",
            "x..x..x"),
        ["Bluetooth"] = Mask(
            "...xx..",
            ".x.x.x.",
            "..xxx..",
            "...x...",
            "..xxx..",
            ".x.x.x.",
            "...xx.."),
        ["Notification"] = Mask(
            "...x...",
            "..xxx..",
            ".xxxxx.",
            ".xxxxx.",
            ".xxxxx.",
            "xxxxxxx",
            "...x..."),
        ["Message"] = Mask(
            "xxxxxxx",
            "x.....x",
            "x.x.x.x",
            "x.....x",
            "xxxxxxx",
            ".xx....",
            ".x....."),
        ["Timer"] = Mask(
            "..xxx..",
            ".x...x.",
            "x..x..x",
            "x..xx.x",
            "x.....x",
            ".x...x.",
            "..xxx.."),
        ["Folder"] = Mask(
            ".......",
            "xxx....",
            "xxxxxxx",
            "x.....x",
            "x.....x",
            "xxxxxxx",
            "......."),
        ["Clipboard"] = Mask(
            "..xxx..",
            "xx...xx",
            "x.xxx.x",
            "x.....x",
            "x.xxx.x",
            "x.....x",
            "xxxxxxx"),
        ["Launcher"] = Mask(
            "xxx.xxx",
            "xxx.xxx",
            "xxx.xxx",
            ".......",
            "xxx.xxx",
            "xxx.xxx",
            "xxx.xxx"),
        ["Search"] = Mask(
            ".xxx...",
            "x...x..",
            "x...x..",
            "x...x..",
            ".xxx...",
            ".....x.",
            "......x"),
        ["Download"] = Mask(
            "...x...",
            "...x...",
            "...x...",
            ".x.x.x.",
            "..xxx..",
            "...x...",
            "xxxxxxx"),
        ["Microphone"] = Mask(
            "..xxx..",
            "..xxx..",
            "..xxx..",
            "x.xxx.x",
            ".x...x.",
            "..xxx..",
            "...x..."),
        ["Camera"] = Mask(
            ".......",
            "xxxxx..",
            "x...x.x",
            "x...xxx",
            "x...x.x",
            "xxxxx..",
            "......."),
        ["Call"] = Mask(
            "xx.....",
            "xxx....",
            ".xx....",
            "..xx...",
            "...xx..",
            "....xxx",
            ".....xx"),
        ["Check"] = Mask(
            ".......",
            "......x",
            ".....x.",
            "x...x..",
            ".x.x...",
            "..x....",
            "......."),
        ["Info"] = Mask(
            "...x...",
            ".......",
            "..xx...",
            "...x...",
            "...x...",
            "...x...",
            "..xxx.."),
        ["Welcome"] = Mask(
            "...x...",
            "...x...",
            "..xxx..",
            "xxxxxxx",
            "..xxx..",
            "...x...",
            "...x..."),
        ["Menu"] = Mask(
            ".......",
            "xxxxxxx",
            ".......",
            "xxxxxxx",
            ".......",
            "xxxxxxx",
            "......."),
        ["Battery"] = Mask(
            ".......",
            "xxxxxx.",
            "x....xx",
            "x....xx",
            "xxxxxx.",
            ".......",
            "......."),
        ["Headphones"] = Mask(
            ".xxxxx.",
            "x.....x",
            "x.....x",
            "xx...xx",
            "xx...xx",
            "xx...xx",
            "......."),
        ["Keyboard"] = Mask(
            ".......",
            "xxxxxxx",
            "x.x.x.x",
            "x.....x",
            "x.xxx.x",
            "xxxxxxx",
            "......."),
        ["Mouse"] = Mask(
            "..xxx..",
            ".x.x.x.",
            ".x.x.x.",
            ".xxxxx.",
            ".x...x.",
            ".x...x.",
            "..xxx.."),
        ["Gamepad"] = Mask(
            ".......",
            ".xxxxx.",
            "xx.xx.x",
            "x...xxx",
            "xx.xx.x",
            "xxxxxxx",
            "xx...xx"),
        ["Play"] = Mask(
            ".x.....",
            ".xx....",
            ".xxx...",
            ".xxxx..",
            ".xxx...",
            ".xx....",
            ".x....."),
        ["Pause"] = Mask(
            ".......",
            ".xx.xx.",
            ".xx.xx.",
            ".xx.xx.",
            ".xx.xx.",
            ".xx.xx.",
            "......."),
        ["Stop"] = Mask(
            ".......",
            ".xxxxx.",
            ".xxxxx.",
            ".xxxxx.",
            ".xxxxx.",
            ".xxxxx.",
            "......."),
        ["Reset"] = Mask(
            "...x...",
            "..xxxx.",
            "...x..x",
            "x.....x",
            "x.....x",
            ".x...x.",
            "..xxx.."),
        ["Previous"] = Mask(
            ".......",
            "x..x..x",
            "x.xx.xx",
            "xxxxxxx",
            "x.xx.xx",
            "x..x..x",
            "......."),
        ["Next"] = Mask(
            ".......",
            "x..x..x",
            "xx.xx.x",
            "xxxxxxx",
            "xx.xx.x",
            "x..x..x",
            "......."),
        ["Settings"] = Mask(
            "...x...",
            ".xxxxx.",
            ".x...x.",
            "xx.x.xx",
            ".x...x.",
            ".xxxxx.",
            "...x..."),
        ["Power"] = Mask(
            "...x...",
            ".x.x.x.",
            "x..x..x",
            "x.....x",
            "x.....x",
            ".x...x.",
            "..xxx.."),
        ["Detach"] = Mask(
            "xxxxxxx",
            ".......",
            "...x...",
            "...x...",
            ".x.x.x.",
            "..xxx..",
            "...x..."),
        ["Dock"] = Mask(
            "....x..",
            "xxxxxx.",
            "....x..",
            "..x....",
            ".xxxxxx",
            "..x....",
            "......."),
        ["ChevronRight"] = Mask(
            ".......",
            "..x....",
            "...x...",
            "....x..",
            "...x...",
            "..x....",
            "......."),
        ["ChevronDown"] = Mask(
            ".......",
            ".......",
            ".x...x.",
            "..x.x..",
            "...x...",
            ".......",
            "......."),
        ["Close"] = Mask(
            ".......",
            ".x...x.",
            "..x.x..",
            "...x...",
            "..x.x..",
            ".x...x.",
            "......."),
        ["Pin"] = Mask(
            "..xxx..",
            "..xxx..",
            "..xxx..",
            ".xxxxx.",
            "...x...",
            "...x...",
            "...x..."),
        ["Delete"] = Mask(
            "..xxx..",
            "xxxxxxx",
            ".x...x.",
            ".x.x.x.",
            ".x.x.x.",
            ".x...x.",
            "..xxx.."),
        ["Home"] = Mask(
            "...x...",
            "..xxx..",
            ".xxxxx.",
            "xxxxxxx",
            ".x...x.",
            ".x.x.x.",
            ".xxxxx."),
        ["Notch"] = Mask(
            "xxxxxxx",
            "x.xxx.x",
            "x.....x",
            "x.....x",
            "x.....x",
            "xxxxxxx",
            "......."),
        ["Palette"] = Mask(
            ".xxxxx.",
            "x.x..xx",
            "xx...xx",
            "x..x..x",
            "xx...x.",
            ".x..x..",
            "..xx..."),
        ["Motion"] = Mask(
            "....xx.",
            "...xx..",
            "..xx...",
            ".xxxxx.",
            "...xx..",
            "..xx...",
            ".x....."),
        ["Display"] = Mask(
            "xxxxxxx",
            "x.....x",
            "x.....x",
            "x.....x",
            "xxxxxxx",
            "...x...",
            ".xxxxx."),
        ["Star"] = Mask(
            "...x...",
            "...x...",
            "xxxxxxx",
            ".xxxxx.",
            "..xxx..",
            ".xx.xx.",
            ".x...x."),
        ["Globe"] = Mask(
            "..xxx..",
            ".x.x.x.",
            "x..x..x",
            "xxxxxxx",
            "x..x..x",
            ".x.x.x.",
            "..xxx.."),
        ["Block"] = Mask(
            "..xxx..",
            ".x...x.",
            "x...x.x",
            "x..x..x",
            "x.x...x",
            ".x...x.",
            "..xxx.."),
        ["Pointer"] = Mask(
            "x......",
            "xx.....",
            "x.x....",
            "x..x...",
            "x.xxx..",
            "xx.x...",
            "....x.."),
        ["Clock"] = Mask(
            "..xxx..",
            ".x.x.x.",
            "x..x..x",
            "x..xx.x",
            "x.....x",
            ".x...x.",
            "..xxx.."),
        ["Fullscreen"] = Mask(
            "xx...xx",
            "x.....x",
            ".......",
            ".......",
            ".......",
            "x.....x",
            "xx...xx"),
        ["Side"] = Mask(
            "xxxxxxx",
            "x...xxx",
            "x...xxx",
            "x...xxx",
            "x...xxx",
            "xxxxxxx",
            "......."),
        ["Bubble"] = Mask(
            ".......",
            "xxxx.xx",
            "xxxx.xx",
            "xxxx.xx",
            ".......",
            ".......",
            "......."),
        ["Stack"] = Mask(
            ".......",
            "..xxxx.",
            ".......",
            ".xxxxx.",
            ".......",
            "xxxxxxx",
            "......."),
        ["Opacity"] = Mask(
            "xxxxxxx",
            "x.x.x.x",
            "xx.x.xx",
            "x.x.x.x",
            "xx.x.xx",
            "x.x.x.x",
            "xxxxxxx"),
        ["Shadow"] = Mask(
            "xxxxx..",
            "x...x..",
            "x...xx.",
            "x...xx.",
            "xxxxxx.",
            "..xxxx.",
            "......."),
        ["Outline"] = Mask(
            "x.x.x.x",
            ".......",
            "x.....x",
            ".......",
            "x.....x",
            ".......",
            "x.x.x.x"),
        ["Density"] = Mask(
            "xxx.xxx",
            "xxx.xxx",
            ".......",
            "xxx.xxx",
            "xxx.xxx",
            ".......",
            "......."),
        ["Corner"] = Mask(
            "..xxxxx",
            ".x.....",
            "x......",
            "x......",
            "x......",
            ".......",
            "......."),
        ["Blend"] = Mask(
            "xxxxxxx",
            ".xxxxx.",
            ".xxxxx.",
            "..xxx..",
            ".......",
            ".......",
            "......."),
        ["Lock"] = Mask(
            "..xxx..",
            ".x...x.",
            ".x...x.",
            "xxxxxxx",
            "xxx.xxx",
            "xxx.xxx",
            "xxxxxxx"),
        ["Bounce"] = Mask(
            "xx.....",
            "..x....",
            "...x.xx",
            "...x.xx",
            "....x..",
            "....x..",
            "xxxxxxx"),
        ["Rain"] = Mask(
            "x..x..x",
            ".......",
            ".x..x..",
            "x..x..x",
            ".......",
            "..x..x.",
            "x..x..x"),
        ["Speed"] = Mask(
            "..xxx..",
            ".x...x.",
            "x....xx",
            "x...x.x",
            "x..x..x",
            ".......",
            "......."),
        ["Drop"] = Mask(
            "...x...",
            "..xxx..",
            ".xxxxx.",
            ".xxxxx.",
            "xxxxxxx",
            ".xxxxx.",
            "..xxx.."),
        ["Magnet"] = Mask(
            "xx...xx",
            "xx...xx",
            "xx...xx",
            "xx...xx",
            "xxx.xxx",
            ".xxxxx.",
            "..xxx.."),
        ["Fade"] = Mask(
            "xxx.x..",
            "xxxx.x.",
            "xxx.x..",
            "xxxx.x.",
            "xxx.x..",
            "xxxx.x.",
            "xxx.x.."),
        ["Bug"] = Mask(
            "x.....x",
            ".x.x.x.",
            "..xxx..",
            "xxxxxxx",
            "..xxx..",
            "xxxxxxx",
            ".x...x."),
        ["Shield"] = Mask(
            "xxxxxxx",
            "x.....x",
            "x..x..x",
            "x.xxx.x",
            ".x.x.x.",
            "..x.x..",
            "...x..."),
        ["Moon"] = Mask(
            "..xxx..",
            ".xx....",
            "xx.....",
            "xx.....",
            "xx....x",
            ".xx..xx",
            "..xxxx."),
        ["Bolt"] = Mask(
            "....xx.",
            "...xx..",
            "..xx...",
            ".xxxxx.",
            "...xx..",
            "..xx...",
            ".xx...."),
        ["Cpu"] = Mask(
            ".x.x.x.",
            "xxxxxxx",
            ".x...x.",
            "xx.x.xx",
            ".x...x.",
            "xxxxxxx",
            ".x.x.x."),
        ["Calendar"] = Mask(
            ".x...x.",
            "xxxxxxx",
            "x.....x",
            "x.x.x.x",
            "x.....x",
            "x.x.x.x",
            "xxxxxxx"),
        ["Qr"] = Mask(
            "xxx.xxx",
            "x.x.x.x",
            "xxx.xxx",
            ".......",
            "xxx.x.x",
            "x.x..x.",
            "xxx.x.x"),
        ["WeatherSun"] = Mask(
            "x..x..x",
            ".x.x.x.",
            "..xxx..",
            "xxxxxxx",
            "..xxx..",
            ".x.x.x.",
            "x..x..x"),
        ["WeatherCloud"] = Mask(
            ".......",
            "..xx...",
            ".xxxxx.",
            "xxxxxxx",
            "xxxxxxx",
            ".......",
            "......."),
        ["WeatherRain"] = Mask(
            "..xx...",
            ".xxxxx.",
            "xxxxxxx",
            ".......",
            ".x..x..",
            "...x..x",
            ".x....."),
        ["WeatherSnow"] = Mask(
            "..xx...",
            ".xxxxx.",
            "xxxxxxx",
            ".......",
            "x.x.x.x",
            ".......",
            ".x.x.x."),
        ["WeatherStorm"] = Mask(
            "..xx...",
            ".xxxxx.",
            "xxxxxxx",
            "...x...",
            "..xx...",
            "...x...",
            "..x...."),
        ["WeatherFog"] = Mask(
            ".......",
            "xxxxxx.",
            ".......",
            ".xxxxxx",
            ".......",
            "xxxxxx.",
            "......."),
        ["Command"] = Mask(
            ".......",
            "x......",
            ".x.....",
            "..x....",
            ".x.....",
            "x..xxxx",
            "......."),
    };

    /// <summary>Clés disposant d'un motif.</summary>
    public static IReadOnlyCollection<string> Keys => Glyphs.Keys;

    /// <summary>
    /// Motif de la clé : 49 cases, rangée par rangée, <c>true</c> pour un pixel
    /// allumé. <c>null</c> si la clé n'a pas de motif.
    /// </summary>
    public static IReadOnlyList<bool>? Resolve(string? key)
        => key is not null && Glyphs.TryGetValue(key, out bool[]? mask) ? mask : null;

    /// <summary>
    /// Rang d'allumage de chaque case : les pixels proches du centre d'abord.
    /// Valeur en « pas » de distance, à multiplier par un délai.
    /// </summary>
    public static double LightOrder(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Size * Size);

        int row = index / Size, column = index % Size;
        double center = (Size - 1) / 2.0;
        return Math.Sqrt(((row - center) * (row - center)) + ((column - center) * (column - center)));
    }

    private static bool[] Mask(params string[] rows)
    {
        if (rows.Length != Size)
        {
            throw new ArgumentException($"Un motif compte {Size} rangées.", nameof(rows));
        }

        var mask = new bool[Size * Size];

        for (int r = 0; r < Size; r++)
        {
            if (rows[r].Length != Size)
            {
                throw new ArgumentException($"Rangée {r} : {Size} colonnes attendues.", nameof(rows));
            }

            for (int c = 0; c < Size; c++)
            {
                mask[(r * Size) + c] = rows[r][c] == 'x';
            }
        }

        return mask;
    }
}
