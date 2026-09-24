using System;
using System.Collections.Generic;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>Une image de la bulle qui naît de la notch : sa place et le fil qui la relie encore.</summary>
/// <param name="Bubble">Rectangle de la bulle à cet instant, en coordonnées d'écran.</param>
/// <param name="Scale">Échelle de la bulle, de <see cref="BubbleBridge.BirthScale"/> à 1.</param>
/// <param name="Bridge">Contours du fil, en coordonnées d'écran, dans le sens horaire.</param>
public readonly record struct BubbleBridgeFrame(ScreenRect Bubble, double Scale, IReadOnlyList<ShapePoint[]> Bridge);

/// <summary>
/// La bulle qui se détache de la notch comme une goutte : elle sort de son
/// flanc, un fil de matière la retient, s'amincit et se rompt, puis elle se
/// pose à sa place. L'échange des rôles et la disparition rejouent la même
/// fonction à l'envers : la bulle rentre dans la notch. Même goutte que
/// l'arrachement (<see cref="GooBridge"/>), couchée dans l'axe qui va de la
/// notch à la bulle. Voir ADR-021.
/// </summary>
public static class BubbleBridge
{
    /// <summary>Durée de la naissance, en secondes.</summary>
    public const double BirthSeconds = 0.38;

    /// <summary>Durée d'un retour dans la notch — disparition ou premier temps d'un échange.</summary>
    public const double MergeSeconds = 0.22;

    /// <summary>Échelle de la bulle quand elle est encore fondue dans la notch.</summary>
    public const double BirthScale = 0.55;

    /// <summary>Part de la bulle enfoncée dans la notch au départ.</summary>
    public const double StartInset = 0.7;

    /// <summary>Épaisseur, le long de l'axe, de la bande de notch d'où part le fil.</summary>
    private const double FlankDepth = 12;

    /// <summary>
    /// Image de la naissance à l'avancement <paramref name="progress"/> : 0, la
    /// bulle est fondue dans le flanc de la notch ; 1, elle est à sa place et le
    /// fil a disparu.
    /// </summary>
    /// <param name="notch">Notch (ou pastille), en coordonnées d'écran.</param>
    /// <param name="final">Place de la bulle au repos.</param>
    /// <param name="progress">Avancement, de 0 à 1.</param>
    /// <param name="notchShoulder">
    /// Épaule de la notch du côté de la bulle : le fil part de son corps, pas de
    /// la pointe de l'épaule, qui n'est qu'un raccord au bord de l'écran.
    /// </param>
    /// <param name="bubbleShoulder">Épaule de la bulle, pour la même raison, de l'autre côté du fil.</param>
    public static BubbleBridgeFrame Frame(
        ScreenRect notch,
        ScreenRect final,
        double progress,
        double notchShoulder = 0,
        double bubbleShoulder = 0)
    {
        double p = Math.Clamp(progress, 0, 1);
        Axis axis = AxisOf(notch, final);

        ScreenRect finalLocal = axis.ToLocal(final);

        // La bulle voyage de l'intérieur du flanc (v négatif) à sa place, avec
        // une décélération : elle est lancée, puis se pose.
        double travel = EaseOut(Math.Clamp(p / 0.75, 0, 1));
        double startV = -finalLocal.Height * StartInset;
        double v = startV + ((finalLocal.Y - startV) * travel);

        double scale = BirthScale + ((1 - BirthScale) * EaseOut(p));
        double width = finalLocal.Width * scale;
        double depth = finalLocal.Height * scale;

        var bubbleLocal = new ScreenRect(
            finalLocal.CenterX - (width / 2),
            v + ((finalLocal.Height - depth) / 2),
            width,
            depth);

        // Le flanc de la notch joue le rôle de la trace accrochée : une bande
        // qui se termine exactement sur le flanc de son corps.
        double inset = Math.Max(0, notchShoulder);
        var flank = new ScreenRect(bubbleLocal.X - 6, -inset - FlankDepth, bubbleLocal.Width + 12, FlankDepth);

        // Et le fil entre dans le corps de la bulle, pas dans son épaule.
        double bodyInset = Math.Max(0, bubbleShoulder) * scale;
        ScreenRect target = bubbleLocal with
        {
            Y = bubbleLocal.Y + bodyInset,
            Height = Math.Max(1, bubbleLocal.Height - (2 * bodyInset))
        };

        var bridge = new List<ShapePoint[]>();

        foreach (ShapePoint[] piece in GooBridge.Neck(flank, target, p))
        {
            bridge.Add(axis.ToScreen(piece));
        }

        return new BubbleBridgeFrame(axis.ToScreenRect(bubbleLocal), scale, bridge);
    }

    private static double EaseOut(double x) => 1 - Math.Pow(1 - Math.Clamp(x, 0, 1), 3);

    /// <summary>
    /// Axe qui va de la notch à la bulle. Dans son repère, <c>u</c> court le
    /// long du flanc et <c>v</c> s'en éloigne : le repère même de
    /// <see cref="GooBridge"/>, où la « trace » est le flanc de la notch.
    /// </summary>
    private readonly record struct Axis(int Direction, ScreenRect Notch)
    {
        // 0 : vers la droite, 1 : vers la gauche, 2 : vers le bas, 3 : vers le haut.
        public ScreenRect ToLocal(ScreenRect r) => Direction switch
        {
            0 => new ScreenRect(r.Y, r.X - Notch.Right, r.Height, r.Width),
            1 => new ScreenRect(r.Y, Notch.X - r.Right, r.Height, r.Width),
            2 => new ScreenRect(r.X, r.Y - Notch.Bottom, r.Width, r.Height),
            _ => new ScreenRect(r.X, Notch.Y - r.Bottom, r.Width, r.Height)
        };

        public ScreenRect ToScreenRect(ScreenRect l) => Direction switch
        {
            0 => new ScreenRect(Notch.Right + l.Y, l.X, l.Height, l.Width),
            1 => new ScreenRect(Notch.X - l.Y - l.Height, l.X, l.Height, l.Width),
            2 => new ScreenRect(l.X, Notch.Bottom + l.Y, l.Width, l.Height),
            _ => new ScreenRect(l.X, Notch.Y - l.Y - l.Height, l.Width, l.Height)
        };

        public ShapePoint[] ToScreen(ShapePoint[] local)
        {
            var points = new ShapePoint[local.Length];

            for (int i = 0; i < local.Length; i++)
            {
                ShapePoint q = local[i];

                points[i] = Direction switch
                {
                    0 => new ShapePoint(Notch.Right + q.Y, q.X),
                    1 => new ShapePoint(Notch.X - q.Y, q.X),
                    2 => new ShapePoint(q.X, Notch.Bottom + q.Y),
                    _ => new ShapePoint(q.X, Notch.Y - q.Y)
                };
            }

            if (GooBridge.SignedArea(points) < 0)
            {
                Array.Reverse(points);
            }

            return points;
        }
    }

    private static Axis AxisOf(ScreenRect notch, ScreenRect bubble)
    {
        if (bubble.X >= notch.Right - 0.5)
        {
            return new Axis(0, notch);
        }

        if (bubble.Right <= notch.X + 0.5)
        {
            return new Axis(1, notch);
        }

        return bubble.Y >= notch.Bottom - 0.5 ? new Axis(2, notch) : new Axis(3, notch);
    }
}
