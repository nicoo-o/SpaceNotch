using System;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.State;

namespace SpaceNotch.Core.Activities;

/// <summary>Forme de l'élément vivant à droite d'une forme compacte.</summary>
public enum TrailingKind
{
    /// <summary>Rien : la mesure texte, s'il y en a une, suffit.</summary>
    None,

    /// <summary>Fil de niveau horizontal : volume, luminosité.</summary>
    Level,

    /// <summary>Anneau de progression : un travail qui avance.</summary>
    Ring,

    /// <summary>Arc ouvert de batterie : un appareil connecté.</summary>
    Battery,

    /// <summary>Barres d'égaliseur qui dansent : une lecture en cours.</summary>
    Equalizer,

    /// <summary>Anneau de sept pixels qui tourne : un travail dont on ignore la fin (M2).</summary>
    Spinner,

    /// <summary>Les pixels du spinner ont glissé en coche : le travail a réussi (M2).</summary>
    Check
}

/// <summary>
/// L'élément vivant de la forme compacte — l'emplacement <em>trailing</em> de la
/// Dynamic Island. L'identité est à gauche (icône, une ligne de texte) ; à
/// droite, un seul élément qui montre que l'activité vit : un niveau, un anneau,
/// un arc de batterie, des barres qui dansent.
///
/// <para>
/// Décidé ici, hors du rendu : le rendu ne devine jamais qu'un casque a une
/// batterie ou qu'une musique joue, il reçoit une forme et une valeur.
/// </para>
/// </summary>
/// <param name="Kind">Forme de l'élément.</param>
/// <param name="Value">Valeur entre 0 et 1 (sans objet pour l'égaliseur).</param>
public readonly record struct CompactTrailing(TrailingKind Kind, double Value)
{
    /// <summary>Aucun élément vivant.</summary>
    public static CompactTrailing None => new(TrailingKind.None, 0);

    /// <summary>L'élément vivant d'une activité.</summary>
    public static CompactTrailing For(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return activity switch
        {
            { Payload: BluetoothPayload { IsConnected: true, BatteryPercent: int battery } }
                => new(TrailingKind.Battery, Math.Clamp(battery / 100.0, 0, 1)),
            { Payload: HudPayload, Progress: double level }
                => new(TrailingKind.Level, Math.Clamp(level, 0, 1)),
            { State: IslandActivityState.MediaActive }
                => new(TrailingKind.Equalizer, 0),
            { Progress: double progress }
                => new(TrailingKind.Ring, Math.Clamp(progress, 0, 1)),
            { MotionState: ActivityMotionState.Completing or ActivityMotionState.Complete, IconKey: "Check" }
                => new(TrailingKind.Check, 1),
            { Role: ActivityRole.Download, MotionState: ActivityMotionState.Working }
                => new(TrailingKind.Spinner, 0),
            _ => None
        };
    }

    /// <summary>Largeur occupée à droite de la mesure texte, en DIPs.</summary>
    public double Width => Kind switch
    {
        TrailingKind.None => 0,
        TrailingKind.Level => 36,
        _ => 16
    };

    /// <summary>
    /// Mesure texte qui accompagne l'élément. Un anneau ou un arc se lit mieux
    /// avec son pourcentage ; un égaliseur n'a rien à dire.
    /// </summary>
    public static string? MetricFor(IslandActivity activity, CompactTrailing trailing)
    {
        ArgumentNullException.ThrowIfNull(activity);

        return trailing.Kind switch
        {
            TrailingKind.Battery => string.Create(System.Globalization.CultureInfo.CurrentCulture, $"{Math.Round(trailing.Value * 100):0} %"),
            TrailingKind.Equalizer or TrailingKind.Check => null,
            _ => activity.TrailingMetric
        };
    }

    /// <summary>
    /// Compteur de pile : « +2 » quand deux autres activités attendent derrière
    /// celle qui est montrée. Un nombre plutôt que des points : il se lit d'un
    /// coup d'œil, et il roule quand il change.
    /// </summary>
    public static string? StackBadge(int activityCount)
        => activityCount > 1
            ? string.Create(System.Globalization.CultureInfo.CurrentCulture, $"+{activityCount - 1}")
            : null;
}
