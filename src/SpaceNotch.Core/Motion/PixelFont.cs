using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Chiffres de l'horloge à palettes (M1) : une police de 3 × 5 pixels, dans la
/// matière du logo. Chaque caractère est une palette ; quand il change, la
/// palette bascule et montre le nouveau.
/// </summary>
public static class PixelFont
{
    /// <summary>Largeur d'un caractère, en pixels.</summary>
    public const int Width = 3;

    /// <summary>Hauteur d'un caractère, en pixels.</summary>
    public const int Height = 5;

    private static readonly Dictionary<char, string> Glyphs = new()
    {
        ['0'] = "xxx" + "x.x" + "x.x" + "x.x" + "xxx",
        ['1'] = ".x." + "xx." + ".x." + ".x." + "xxx",
        ['2'] = "xxx" + "..x" + "xxx" + "x.." + "xxx",
        ['3'] = "xxx" + "..x" + ".xx" + "..x" + "xxx",
        ['4'] = "x.x" + "x.x" + "xxx" + "..x" + "..x",
        ['5'] = "xxx" + "x.." + "xxx" + "..x" + "xxx",
        ['6'] = "xxx" + "x.." + "xxx" + "x.x" + "xxx",
        ['7'] = "xxx" + "..x" + ".x." + ".x." + ".x.",
        ['8'] = "xxx" + "x.x" + "xxx" + "x.x" + "xxx",
        ['9'] = "xxx" + "x.x" + "xxx" + "..x" + "xxx",
        [':'] = "..." + ".x." + "..." + ".x." + "...",
        [' '] = "..." + "..." + "..." + "..." + "...",
    };

    /// <summary>Le caractère a-t-il un dessin ?</summary>
    public static bool Supports(char c) => Glyphs.ContainsKey(c);

    /// <summary>
    /// Pixels du caractère, rangée par rangée (15 cases). Un caractère inconnu
    /// est dessiné vide plutôt que de lever : l'horloge ne doit jamais tomber.
    /// </summary>
    public static IReadOnlyList<bool> Resolve(char c)
    {
        string mask = Glyphs.TryGetValue(c, out string? m) ? m : Glyphs[' '];
        var cells = new bool[Width * Height];

        for (int i = 0; i < cells.Length; i++)
        {
            cells[i] = mask[i] == 'x';
        }

        return cells;
    }

    /// <summary>
    /// Indices des palettes qui doivent basculer entre deux affichages : seules
    /// celles dont le caractère change bougent, jamais tout le cadran.
    /// </summary>
    public static IReadOnlyList<int> Changed(string? before, string after)
    {
        ArgumentNullException.ThrowIfNull(after);
        var changed = new List<int>();

        for (int i = 0; i < after.Length; i++)
        {
            if (before is null || i >= before.Length || before[i] != after[i])
            {
                changed.Add(i);
            }
        }

        return changed;
    }
}
