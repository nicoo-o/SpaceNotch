using System;
using System.Collections.Generic;

namespace SpaceNotch.Core.Scenes;

/// <summary>
/// Déformations du contour de la notch, appliquées au contour que
/// <see cref="IslandShape.Silhouette"/> vient de calculer.
///
/// <para>
/// <b>Goutte (P2).</b> Un fichier survole la notch : son bord bas se liquéfie
/// et une goutte noire pend vers le curseur. Le corps est tracé un peu moins
/// haut que la fenêtre et la goutte descend dans la marge ainsi libérée : rien
/// ne sort de la fenêtre, rien n'est dessiné autour de la notch (E1). Au dépôt,
/// la goutte remonte et le bord ondule, une onde qui part du point de chute et
/// s'amortit.
/// </para>
/// </summary>
public static class ShapeEffects
{
    /// <summary>Profondeur de la goutte, en DIP : la marge réservée sous le corps.</summary>
    public const double DropDepth = 12;

    /// <summary>Demi-largeur de la goutte, en DIP.</summary>
    public const double DropSpread = 13;

    /// <summary>Pas d'échantillonnage du bord liquide, en DIP.</summary>
    public const double Sample = 2;

    /// <summary>Longueur d'onde de l'ondulation, en DIP.</summary>
    public const double RippleWavelength = 46;

    /// <summary>Distance, en DIP, sur laquelle l'onde s'amortit autour du point de chute.</summary>
    public const double RippleReach = 120;

    /// <summary>
    /// Profil du bord liquide, mesuré vers le bas depuis le bas du corps, en
    /// DIP : la goutte plus l'onde. Toujours entre −<paramref name="ripple"/> et
    /// <see cref="DropDepth"/> : le bord ne sort jamais de la fenêtre.
    /// </summary>
    /// <param name="x">Abscisse, en DIP.</param>
    /// <param name="dropX">Abscisse de la goutte ou du point de chute.</param>
    /// <param name="drop">Avancement de la goutte, de 0 (rentrée) à 1 (pendue).</param>
    /// <param name="ripple">Amplitude de l'onde, en DIP (au plus <see cref="DropDepth"/>).</param>
    /// <param name="phase">Phase de l'onde, en radians : elle avance avec le temps.</param>
    public static double Profile(double x, double dropX, double drop, double ripple, double phase)
    {
        double d = x - dropX;
        double hang = DropDepth * Math.Clamp(drop, 0, 1) * Math.Exp(-(d * d) / (DropSpread * DropSpread));
        double amp = Math.Clamp(ripple, 0, DropDepth);
        double wave = amp * Math.Sin((Math.Abs(d) * 2 * Math.PI / RippleWavelength) - phase) * Math.Exp(-Math.Abs(d) / RippleReach);

        return Math.Clamp(hang + wave, -amp, DropDepth);
    }

    /// <summary>
    /// Remplace le bord bas droit d'un contour par le bord liquide. Le contour
    /// doit avoir été calculé pour la hauteur du corps (<paramref name="bodyHeight"/>),
    /// soit la hauteur de la fenêtre moins <see cref="DropDepth"/>.
    /// </summary>
    public static ShapePoint[] Liquid(ShapePoint[] outline, double bodyHeight, double dropX, double drop, double ripple, double phase)
    {
        ArgumentNullException.ThrowIfNull(outline);

        if (outline.Length < 2 || (drop <= 0 && ripple <= 0))
        {
            return outline;
        }

        var result = new List<ShapePoint>(outline.Length + 128);

        for (int i = 0; i < outline.Length; i++)
        {
            ShapePoint a = outline[i];
            ShapePoint b = outline[(i + 1) % outline.Length];
            result.Add(a);

            // Le bord bas : deux sommets consécutifs à la hauteur du corps, du
            // congé droit vers le congé gauche (sens horaire).
            bool bottom = Math.Abs(a.Y - bodyHeight) < 0.01 && Math.Abs(b.Y - bodyHeight) < 0.01 && a.X - b.X > Sample;

            if (!bottom)
            {
                continue;
            }

            for (double x = a.X - Sample; x > b.X; x -= Sample)
            {
                result.Add(new ShapePoint(x, bodyHeight + Profile(x, dropX, drop, ripple, phase)));
            }
        }

        return [.. result];
    }
}
