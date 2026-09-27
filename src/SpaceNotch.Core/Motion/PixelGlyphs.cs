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
