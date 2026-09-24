using System;
using System.Collections.Generic;
using System.Linq;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>
/// La notch qui se partage : une bulle à côté d'elle, pour une activité
/// importante qui ne doit pas disparaître derrière celle qui est présentée.
///
/// <para>
/// Le partage est réservé à ce qui compte — téléchargement, appel,
/// enregistrement, priorité critique. Deux activités ordinaires ne partagent
/// jamais la notch : la seconde attend dans la pile, signalée par les points.
/// Toucher la bulle échange les rôles : son activité prend la notch, l'autre
/// devient la bulle. Voir ADR-019.
/// </para>
/// </summary>
public static class SplitPresentation
{
    /// <summary>Largeur de la bulle hors épaules, en DIPs.</summary>
    public const double BubbleBody = 36;

    /// <summary>Hauteur de la bulle accrochée : celle de la notch compacte.</summary>
    public const double BubbleHeight = 36;

    /// <summary>Épaules de la bulle : proportionnées à sa taille, plus fines que celles de la notch.</summary>
    public const double BubbleShoulder = 9;

    /// <summary>Espace entre la notch et la bulle, épaule à épaule, en DIPs.</summary>
    public const double Gap = 8;

    /// <summary>Diamètre de la bulle flottante, qui suit une notch détachée.</summary>
    public const double FloatingDiameter = 36;

    /// <summary>Encombrement de la bulle accrochée au bord, épaules comprises.</summary>
    public static IslandFootprint AttachedBubble => new(BubbleBody + (2 * BubbleShoulder), BubbleHeight);

    /// <summary>Encombrement de la bulle qui suit une notch détachée.</summary>
    public static IslandFootprint FloatingBubble => new(FloatingDiameter, FloatingDiameter);

    /// <summary>Vrai si l'activité mérite de partager la notch.</summary>
    public static bool IsImportant(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return activity.Role != ActivityRole.None || activity.Priority == ActivityPriority.Critical;
    }

    /// <summary>
    /// Activité à montrer dans la bulle, ou <c>null</c> si la notch reste entière.
    ///
    /// <para>
    /// Un retour système, une notification — tout ce qui est temporaire — passe
    /// par-dessus la notch et ne prend jamais de bulle. Parmi le reste, la bulle
    /// va d'abord à une activité importante que la notch ne montre pas ; si
    /// c'est la notch qui montre l'activité importante, la bulle garde la
    /// suivante de la pile : c'est ce qui rend l'échange réversible.
    /// </para>
    /// </summary>
    /// <param name="presented">Activité présentée par la notch.</param>
    /// <param name="active">Activités actives, dans n'importe quel ordre.</param>
    public static IslandActivity? BubbleFor(IslandActivity? presented, IEnumerable<IslandActivity> active)
    {
        ArgumentNullException.ThrowIfNull(active);

        if (presented is null)
        {
            return null;
        }

        List<IslandActivity> others = active
            .Where(a => !string.Equals(a.Id, presented.Id, StringComparison.Ordinal))
            .Where(a => ActivityPolicies.Resolve(a) != ActivityPresentationPolicy.Temporary)
            .OrderByDescending(a => a.Priority)
            .ThenByDescending(a => a.CreatedAt)
            .ToList();

        IslandActivity? important = others.FirstOrDefault(IsImportant);

        if (important is not null)
        {
            return important;
        }

        return IsImportant(presented) ? others.FirstOrDefault() : null;
    }

    /// <summary>
    /// Place de la bulle accrochée : à droite de la notch, épaule contre épaule,
    /// ou à gauche si la droite de l'écran manque de place.
    /// </summary>
    /// <param name="notch">Notch accrochée, en coordonnées d'écran.</param>
    /// <param name="screen">Moniteur, en coordonnées d'écran.</param>
    public static ScreenRect AttachedBubbleRect(ScreenRect notch, ScreenRect screen)
    {
        IslandFootprint bubble = AttachedBubble;
        double x = notch.Right + Gap;

        if (x + bubble.Width > screen.Right)
        {
            x = notch.X - Gap - bubble.Width;
        }

        return new ScreenRect(x, notch.Y, bubble.Width, bubble.Height);
    }

    /// <summary>
    /// Place visée par la bulle flottante : à droite de la pastille, centrée sur
    /// sa hauteur, ou à gauche près du bord droit. Le ressort qui la fait suivre
    /// lui donne son retard ; cette fonction ne dit que la destination.
    /// </summary>
    public static ScreenRect FloatingBubbleRect(ScreenRect pill, ScreenRect work)
    {
        IslandFootprint bubble = FloatingBubble;
        double x = pill.Right + Gap;

        if (x + bubble.Width > work.Right - Detachment.EdgeMargin)
        {
            x = pill.X - Gap - bubble.Width;
        }

        return new ScreenRect(x, pill.CenterY - (bubble.Height / 2), bubble.Width, bubble.Height);
    }
}
