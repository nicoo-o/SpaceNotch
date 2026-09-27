using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Petites scènes en pixels (F12, F1) : à la connexion d'un appareil, son
/// icône joue une seconde — le boîtier des écouteurs s'ouvre, la manette
/// vibre, le clavier tape une touche, la souris clique — puis revient à
/// l'icône habituelle. Au branchement du chargeur, la batterie se remplit
/// jusqu'au niveau réel puis laisse place à l'éclair.
///
/// <para>
/// Chaque image est un motif de 7 × 7 comme <see cref="PixelGlyphs"/> ; la
/// dernière image est toujours l'icône de repos, pour que rien ne saute à la
/// fin de l'animation.
/// </para>
/// </summary>
public static class DeviceAnimation
{
    /// <summary>Durée d'une image, en millisecondes.</summary>
    public const int FrameMilliseconds = 110;

    private const int Size = PixelGlyphs.Size;

    /// <summary>
    /// Images de la connexion d'un appareil, selon son type (« audio »,
    /// « gamepad », « keyboard », « mouse », « phone », autre).
    /// </summary>
    public static IReadOnlyList<bool[]> Connect(string? kind)
    {
        switch (kind)
        {
            case "audio":
                return
                [
                    Mask(".......", ".......", ".xxxxx.", ".x...x.", ".x...x.", ".xxxxx.", "......."),
                    Mask(".......", ".xxxxx.", ".......", ".x...x.", ".x...x.", ".xxxxx.", "......."),
                    Mask(".xxxxx.", ".......", ".......", ".x...x.", ".x...x.", ".xxxxx.", "......."),
                    Mask(".xxxxx.", ".......", "..x.x..", ".xx.xx.", ".x...x.", ".xxxxx.", "......."),
                    Mask(".xxxxx.", "..x.x..", "..x.x..", ".x...x.", ".x...x.", ".xxxxx.", "......."),
                    Mask(".xxxxx.", "..x.x..", "..x.x..", ".x...x.", ".x...x.", ".xxxxx.", "......."),
                    Rest("Headphones")
                ];

            case "gamepad":
                return Shake("Gamepad");

            case "phone":
                return Shake("Call");

            case "keyboard":
            {
                bool[] rest = Rest("Keyboard");
                return [Press(rest, (2, 2)), rest, Press(rest, (2, 4)), rest, Press(rest, (4, 2), (4, 3), (4, 4)), rest];
            }

            case "mouse":
            {
                bool[] rest = Rest("Mouse");
                return [Press(rest, (1, 2), (2, 2)), rest, Press(rest, (1, 2), (2, 2)), rest, Press(rest, (1, 3), (2, 3)), rest];
            }

            default:
            {
                bool[] rest = Rest("Bluetooth");
                var empty = new bool[Size * Size];
                return [empty, rest, empty, rest];
            }
        }
    }

    /// <summary>
    /// Images du branchement du chargeur : la batterie se remplit colonne par
    /// colonne jusqu'au niveau <paramref name="percent"/>, puis l'éclair.
    /// </summary>
    public static IReadOnlyList<bool[]> Charging(int percent)
    {
        bool[] body = Rest("Battery");
        int columns = FillColumns(percent);
        var frames = new List<bool[]> { body };

        for (int filled = 1; filled <= columns; filled++)
        {
            var frame = (bool[])body.Clone();

            for (int c = 0; c < filled; c++)
            {
                frame[(2 * Size) + 1 + c] = true;
                frame[(3 * Size) + 1 + c] = true;
            }

            frames.Add(frame);
            frames.Add(frame);
        }

        frames.Add(Rest("Bolt"));
        return frames;
    }

    /// <summary>Colonnes remplies (0 à 4) à un niveau de charge.</summary>
    public static int FillColumns(int percent) => (int)Math.Ceiling(Math.Clamp(percent, 0, 100) / 25.0);

    private static bool[] Rest(string key)
        => (bool[])((PixelGlyphs.Resolve(key) as bool[]) ?? new bool[Size * Size]).Clone();

    /// <summary>L'icône tremble d'un pixel à gauche et à droite.</summary>
    private static IReadOnlyList<bool[]> Shake(string key)
    {
        bool[] rest = Rest(key);
        bool[] left = Shift(rest, -1), right = Shift(rest, 1);
        return [left, right, left, right, rest];
    }

    private static bool[] Shift(bool[] mask, int dx)
    {
        var shifted = new bool[mask.Length];

        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                int from = c - dx;

                if (from >= 0 && from < Size)
                {
                    shifted[(r * Size) + c] = mask[(r * Size) + from];
                }
            }
        }

        return shifted;
    }

    /// <summary>Une touche enfoncée : ses pixels s'éteignent un instant.</summary>
    private static bool[] Press(bool[] rest, params (int Row, int Column)[] keys)
    {
        var frame = (bool[])rest.Clone();

        foreach ((int row, int column) in keys)
        {
            frame[(row * Size) + column] = !frame[(row * Size) + column];
        }

        return frame;
    }

    private static bool[] Mask(params string[] rows)
    {
        var mask = new bool[Size * Size];

        for (int r = 0; r < Size; r++)
        {
            for (int c = 0; c < Size; c++)
            {
                mask[(r * Size) + c] = rows[r][c] == 'x';
            }
        }

        return mask;
    }
}
