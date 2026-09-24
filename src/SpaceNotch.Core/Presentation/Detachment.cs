using System;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>Rectangle en DIPs, dans le repère de l'écran (origine en haut à gauche de la zone de travail ou du moniteur).</summary>
public readonly record struct ScreenRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + (Width / 2);

    public double CenterY => Y + (Height / 2);

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public static ScreenRect Centered(double centerX, double centerY, double width, double height)
        => new(centerX - (width / 2), centerY - (height / 2), width, height);

    /// <summary>Même rectangle, ramené à l'intérieur de <paramref name="bounds"/> avec une marge.</summary>
    public ScreenRect ClampInside(ScreenRect bounds, double margin = 0)
    {
        double minX = bounds.X + margin;
        double minY = bounds.Y + margin;
        double maxX = bounds.Right - margin - Width;
        double maxY = bounds.Bottom - margin - Height;

        double x = maxX < minX ? bounds.CenterX - (Width / 2) : Math.Clamp(X, minX, maxX);
        double y = maxY < minY ? bounds.CenterY - (Height / 2) : Math.Clamp(Y, minY, maxY);

        return this with { X = x, Y = y };
    }
}

/// <summary>Où la notch est posée.</summary>
public enum NotchAttachment
{
    /// <summary>Accrochée au bord supérieur : la forme de référence (ADR-017).</summary>
    Attached = 0,

    /// <summary>Arrachée par l'utilisateur et posée librement sur l'écran (ADR-019).</summary>
    Floating = 1
}

/// <summary>Issue d'un lâcher de la notch flottante.</summary>
public enum FloatingLanding
{
    /// <summary>Lâchée sans élan : elle reste où elle a été posée.</summary>
    Stay,

    /// <summary>Lancée : elle glisse jusqu'à l'aimant le plus proche du point projeté.</summary>
    Magnet,

    /// <summary>Ramenée vers le haut de l'écran : elle s'y accroche de nouveau.</summary>
    Reattach
}

/// <summary>Destination d'un lâcher : l'issue, et le coin supérieur gauche visé pour la pastille.</summary>
public readonly record struct FloatingTarget(FloatingLanding Landing, double X, double Y);

/// <summary>Sens d'ouverture d'une notch flottante.</summary>
public enum FloatingExpansion
{
    /// <summary>Elle grandit vers le bas, son bord haut reste en place.</summary>
    Down,

    /// <summary>Elle grandit vers le haut, son bord bas reste en place.</summary>
    Up
}

/// <summary>
/// Règles du détachement : quand la notch se laisse arracher, comment elle
/// résiste, où elle se pose, dans quel sens elle s'ouvre. Toutes les valeurs
/// sont en DIPs. Voir ADR-019.
/// </summary>
public static class Detachment
{
    /// <summary>
    /// En deçà, un appui qui bouge reste un clic. Six DIPs couvrent le tremblé
    /// d'une souris et d'un pavé tactile sans retarder un vrai glisser.
    /// </summary>
    public const double ClickSlop = 6;

    /// <summary>Distance de tirage vers le bas au-delà de laquelle la notch s'arrache.</summary>
    public const double TearDistance = 40;

    /// <summary>Allongement visuel asymptotique pendant le tirage.</summary>
    public const double PullDimension = 90;

    /// <summary>Hauteur, sous le bord supérieur, où un lâcher raccroche la notch.</summary>
    public const double ReattachZone = 64;

    /// <summary>
    /// Part de la largeur de l'écran, de part et d'autre de l'emplacement
    /// d'accroche, où le raccrochage est possible. Hors de cette bande, le haut
    /// de l'écran appartient aux aimants de coin.
    /// </summary>
    public const double ReattachBand = 0.25;

    /// <summary>Vitesse sous laquelle un lâcher est une pose, pas un lancer.</summary>
    public const double FlingSpeed = 300;

    /// <summary>Marge entre la pastille et les bords de l'écran, aux aimants.</summary>
    public const double MagnetMargin = 16;

    /// <summary>Marge minimale entre une notch posée et les bords de l'écran.</summary>
    public const double EdgeMargin = 8;

    /// <summary>
    /// Allongement de la notch accrochée qu'on tire vers le bas : la matière
    /// résiste, et de plus en plus. Tirer vers le haut ne fait rien.
    /// </summary>
    public static double PullStretch(double pullDown)
        => pullDown <= 0 ? 0 : FluidMotion.RubberBand(pullDown, PullDimension);

    /// <summary>Vrai quand le tirage vers le bas suffit à arracher la notch.</summary>
    public static bool ShouldTear(double pullDown) => pullDown >= TearDistance;

    /// <summary>Vrai quand le déplacement depuis l'appui cesse d'être un clic.</summary>
    public static bool ExceedsClickSlop(double dx, double dy)
        => (dx * dx) + (dy * dy) > ClickSlop * ClickSlop;

    /// <summary>
    /// Forme accrochée étirée par le tirage : plus haute, un peu plus étroite —
    /// l'aire reste à peu près constante, comme une goutte qui s'allonge.
    /// </summary>
    public static IslandFootprint Pulled(IslandFootprint attached, double pullDown)
    {
        double stretch = PullStretch(pullDown);

        if (stretch <= 0 || !attached.IsValid)
        {
            return attached;
        }

        double height = attached.Height + stretch;
        double width = attached.Width * Math.Sqrt(attached.Height / height);

        return new IslandFootprint(Math.Max(width, attached.Width * 0.82), height);
    }

    /// <summary>
    /// Pastille flottante correspondant à une forme accrochée : les épaules
    /// appartiennent au bord de l'écran, elles ne partent pas avec la notch.
    /// </summary>
    public static IslandFootprint FloatingOf(IslandFootprint attached, double shoulder)
    {
        double s = IslandShape.EffectiveShoulder(attached.Width, attached.Height, shoulder);

        return new IslandFootprint(Math.Max(attached.Height, attached.Width - (2 * s)), attached.Height);
    }

    /// <summary>
    /// Où se pose la pastille lâchée.
    ///
    /// <para>
    /// L'élan est projeté comme un défilement (<see cref="FluidMotion.Project"/>),
    /// puis la destination est choisie sur ce point projeté et non sur le point
    /// de lâcher : c'est ce qui permet de « lancer » la notch vers un coin, comme
    /// l'image dans l'image de l'iPhone.
    /// </para>
    /// </summary>
    /// <param name="pill">Pastille au moment du lâcher.</param>
    /// <param name="velocityX">Vitesse horizontale au lâcher, DIPs par seconde.</param>
    /// <param name="velocityY">Vitesse verticale au lâcher, DIPs par seconde.</param>
    /// <param name="work">Zone de travail du moniteur.</param>
    /// <param name="attachCenterX">Abscisse du centre de la notch quand elle est accrochée.</param>
    public static FloatingTarget Land(
        ScreenRect pill,
        double velocityX,
        double velocityY,
        ScreenRect work,
        double attachCenterX)
    {
        double speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        bool fling = speed >= FlingSpeed;

        ScreenRect projected = fling
            ? pill with
            {
                X = pill.X + FluidMotion.Project(velocityX),
                Y = pill.Y + FluidMotion.Project(velocityY)
            }
            : pill;

        if (ReachesReattach(projected, work, attachCenterX))
        {
            return new FloatingTarget(FloatingLanding.Reattach, attachCenterX - (pill.Width / 2), work.Y);
        }

        if (!fling)
        {
            ScreenRect rest = pill.ClampInside(work, EdgeMargin);
            return new FloatingTarget(FloatingLanding.Stay, rest.X, rest.Y);
        }

        (double x, double y) = NearestMagnet(projected, work);
        return new FloatingTarget(FloatingLanding.Magnet, x, y);
    }

    /// <summary>
    /// Vrai si la pastille, à cet endroit, demande à se raccrocher : son bord
    /// haut est dans la zone de raccrochage, et son centre dans la bande
    /// centrale de l'écran.
    /// </summary>
    public static bool ReachesReattach(ScreenRect pill, ScreenRect work, double attachCenterX)
        => pill.Y - work.Y < ReattachZone
            && Math.Abs(pill.CenterX - attachCenterX) <= work.Width * ReattachBand;

    /// <summary>
    /// Aimant le plus proche : quatre coins, milieux des côtés gauche et droit,
    /// milieu du bas. Le milieu du haut n'est pas un aimant, c'est l'accroche.
    /// </summary>
    public static (double X, double Y) NearestMagnet(ScreenRect projected, ScreenRect work)
    {
        double left = work.X + MagnetMargin;
        double right = work.Right - MagnetMargin - projected.Width;
        double top = work.Y + MagnetMargin;
        double bottom = work.Bottom - MagnetMargin - projected.Height;
        double middleX = work.CenterX - (projected.Width / 2);
        double middleY = work.CenterY - (projected.Height / 2);

        ReadOnlySpan<(double X, double Y)> magnets =
        [
            (left, top),
            (right, top),
            (left, bottom),
            (right, bottom),
            (left, middleY),
            (right, middleY),
            (middleX, bottom)
        ];

        (double X, double Y) best = magnets[0];
        double bestDistance = double.MaxValue;

        foreach ((double x, double y) in magnets)
        {
            double dx = x - projected.X;
            double dy = y - projected.Y;
            double distance = (dx * dx) + (dy * dy);

            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = (x, y);
            }
        }

        return best;
    }

    /// <summary>
    /// Sens d'ouverture : vers l'espace libre. Posée dans la moitié haute, la
    /// notch s'ouvre vers le bas ; dans la moitié basse, vers le haut.
    /// </summary>
    public static FloatingExpansion ExpansionFor(ScreenRect pill, ScreenRect work)
        => pill.CenterY <= work.CenterY ? FloatingExpansion.Down : FloatingExpansion.Up;

    /// <summary>
    /// Rectangle d'une notch flottante pour un encombrement donné — repos,
    /// aperçu, ouverture ou n'importe quelle image du ressort entre les deux.
    ///
    /// <para>
    /// Le bord qui ne bouge pas est celui du côté opposé à l'ouverture, et
    /// l'axe vertical passe par le centre de la pastille : la forme grandit
    /// depuis l'endroit où l'utilisateur l'a posée, puis se décale seulement si
    /// elle toucherait un bord.
    /// </para>
    /// </summary>
    public static ScreenRect Anchor(ScreenRect pill, IslandFootprint footprint, ScreenRect work)
    {
        double y = ExpansionFor(pill, work) == FloatingExpansion.Down
            ? pill.Y
            : pill.Bottom - footprint.Height;

        var rect = new ScreenRect(pill.CenterX - (footprint.Width / 2), y, footprint.Width, footprint.Height);

        return rect.ClampInside(work, EdgeMargin);
    }
}
