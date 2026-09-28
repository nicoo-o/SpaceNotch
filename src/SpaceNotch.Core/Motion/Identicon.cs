using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Motion;

/// <summary>
/// Identicône générative (A9) : une app sans icône — un script, un .exe maison —
/// reçoit une grille 5 × 5 symétrique tirée de son nom, dans la matière pixel
/// de la notch. Elle reste la même d'une fois sur l'autre : on reconnaît ses
/// outils sans lire leur nom.
/// </summary>
public static class Identicon
{
    /// <summary>Côté de la grille.</summary>
    public const int Size = 5;

    /// <summary>Délai de pousse par case de distance au centre, en millisecondes.</summary>
    public const int GrowStepMilliseconds = 60;

    /// <summary>
    /// Motif pour un nom : 25 cases, symétrique gauche-droite, jamais vide ni
    /// plein (une grille pleine ou vide ne distinguerait rien). La casse et les
    /// espaces autour du nom ne comptent pas.
    /// </summary>
    public static bool[] From(string? name)
    {
        string key = (name ?? string.Empty).Trim().ToUpperInvariant();
        uint hash = 2166136261;

        foreach (char c in key)
        {
            hash ^= c;
            hash = unchecked(hash * 16777619);
        }

        var mask = new bool[Size * Size];
        int lit = 0;

        for (int row = 0; row < Size; row++)
        {
            for (int column = 0; column <= Size / 2; column++)
            {
                int bit = (row * 3) + column;
                bool on = ((hash >> (bit % 32)) & 1) == 1;
                mask[(row * Size) + column] = on;
                mask[(row * Size) + (Size - 1 - column)] = on;
            }
        }

        foreach (bool b in mask)
        {
            lit += b ? 1 : 0;
        }

        // Garde : un motif vide reçoit une croix, un motif plein perd ses coins.
        if (lit == 0)
        {
            foreach (int i in new[] { 2, 7, 10, 11, 12, 13, 14, 17, 22 })
            {
                mask[i] = true;
            }
        }
        else if (lit == mask.Length)
        {
            foreach (int i in new[] { 0, 4, 20, 24 })
            {
                mask[i] = false;
            }
        }

        return mask;
    }

    /// <summary>Teinte du motif, choisie dans la palette des activités à partir du nom.</summary>
    public static int PaletteIndex(string? name, int paletteSize)
    {
        if (paletteSize <= 0)
        {
            return 0;
        }

        int sum = 0;
        foreach (char c in (name ?? string.Empty).Trim().ToUpperInvariant())
        {
            sum = unchecked((sum * 31) + c);
        }

        return (int)((uint)sum % (uint)paletteSize);
    }

    /// <summary>Délai de pousse d'une case, en millisecondes : du centre vers les bords.</summary>
    public static int GrowDelay(int index)
    {
        int row = index / Size, column = index % Size;
        double d = Math.Sqrt(((row - 2) * (row - 2)) + ((column - 2) * (column - 2)));
        return (int)Math.Round(d * GrowStepMilliseconds);
    }

    /// <summary>Cases allumées d'un motif.</summary>
    public static IEnumerable<int> Lit(bool[] mask)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            if (mask[i])
            {
                yield return i;
            }
        }
    }
}
