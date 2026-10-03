using System;
using System.Collections.Generic;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Presentation;

/// <summary>Un geste possible et ce qu'il fait : « Molette · volume ».</summary>
public readonly record struct GestureTip(string Gesture, string Effect);

/// <summary>
/// Aide des gestes (vague 7) : survolée avec Alt enfoncé, la notch liste les
/// gestes qui ont un effet sur ce qu'elle montre. Trois au plus, les plus
/// utiles d'abord : une aide qui défile n'aide plus.
/// </summary>
public static class GestureHelp
{
    /// <summary>Nombre maximal de gestes montrés.</summary>
    public const int MaxTips = 3;

    /// <summary>Les gestes utiles pour ce qui est présenté (null : le repos).</summary>
    public static IReadOnlyList<GestureTip> For(IslandActivity? activity)
    {
        var tips = new List<GestureTip>();

        if (activity is null)
        {
            // Sans musique, la molette ne règle plus le volume (phase B) : elle
            // n'est plus annoncée au repos.
            tips.Add(new(Lang.T("Clic ou tirer ↓", "Click or pull ↓"), Lang.T("recherche", "search")));
            tips.Add(new(Lang.T("Double-clic", "Double-click"), Lang.T("note", "note")));
            tips.Add(new(Lang.T("Clic droit", "Right click"), Lang.T("menu", "menu")));
            return tips;
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Media, StringComparison.Ordinal))
        {
            tips.Add(new(Lang.T("Molette", "Wheel"), Lang.T("volume", "volume")));
            tips.Add(new(Lang.T("Clic du milieu", "Middle click"), Lang.T("pause", "pause")));
            tips.Add(new(Lang.T("Glisser ↔", "Swipe ↔"), Lang.T("morceau", "track")));
            return tips;
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Clipboard, StringComparison.Ordinal))
        {
            tips.Add(new(Lang.T("Ctrl + molette", "Ctrl + wheel"), Lang.T("la pile", "the stack")));
            tips.Add(new(Lang.T("Glisser ←", "Swipe ←"), Lang.T("supprimer", "delete")));
            tips.Add(new(Lang.T("Clic", "Click"), Lang.T("recoller", "paste again")));
            return tips;
        }

        tips.Add(new(Lang.T("Clic ou tirer ↓", "Click or pull ↓"), Lang.T("ouvrir", "open")));

        if (activity.Actions.Count > 0)
        {
            tips.Add(new(Lang.T("Entrée", "Enter"), activity.Actions[0].Label.ToLowerInvariant()));
        }

        tips.Add(new(Lang.T("Clic droit", "Right click"), Lang.T("menu", "menu")));
        return tips.Count > MaxTips ? tips.GetRange(0, MaxTips) : tips;
    }
}

/// <summary>
/// Gestes de la notch accrochée (RFC §3.3, phase B) : à quoi sert la molette,
/// quand un appui au doigt devient un appui long, quand l'aide se montre.
/// </summary>
public static class NotchGestures
{
    /// <summary>Durée d'un appui long au doigt ou au stylet, en secondes : il ouvre le menu rapide.</summary>
    public const double LongPressSeconds = 0.5;

    /// <summary>Temps pendant lequel Alt doit rester enfoncé au survol avant que l'aide se montre.</summary>
    public static readonly TimeSpan HelpDelay = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Vrai si la molette règle le volume : seulement quand une musique (ou le
    /// témoin de volume) est présentée, ou quand la notch est ouverte. Sinon,
    /// défiler sur les onglets du navigateur, juste sous la notch, changeait le
    /// volume du système.
    /// </summary>
    public static bool WheelControlsVolume(string? presentedFeatureId, bool open)
        => open
            || string.Equals(presentedFeatureId, Features.FeatureKeys.Media, StringComparison.Ordinal)
            || string.Equals(presentedFeatureId, Features.FeatureKeys.VolumeHud, StringComparison.Ordinal);

    /// <summary>
    /// Vrai si un appui relâché sans avoir bougé est un appui long : au doigt
    /// ou au stylet seulement (à la souris, le clic droit existe), tenu au
    /// moins <see cref="LongPressSeconds"/>.
    /// </summary>
    public static bool IsLongPress(bool touchOrPen, double heldSeconds)
        => touchOrPen && heldSeconds >= LongPressSeconds;
}

/// <summary>
/// Annuler en 3 s (vague 7) : ce que l'utilisateur vient d'écarter (« Ignorer »,
/// « Refuser », une suppression) reste rattrapable un court instant. Une
/// seule chose à la fois : la dernière écartée.
/// </summary>
public sealed class UndoShelf
{
    /// <summary>Le temps pendant lequel on peut annuler.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(3);

    private IslandActivity? _held;
    private DateTimeOffset _until;

    /// <summary>Garde l'activité écartée, jusqu'à l'échéance.</summary>
    public void Offer(IslandActivity activity, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(activity);
        _held = activity;
        _until = now + Window;
    }

    /// <summary>Vrai tant qu'une annulation est possible.</summary>
    public bool CanUndo(DateTimeOffset now) => _held is not null && now < _until;

    /// <summary>Part du temps restant (1 → 0), pour l'anneau qui se vide.</summary>
    public double Remaining(DateTimeOffset now)
        => CanUndo(now) ? Math.Clamp((_until - now) / Window, 0, 1) : 0;

    /// <summary>Rend l'activité écartée si l'échéance n'est pas passée, puis l'oublie.</summary>
    public IslandActivity? Take(DateTimeOffset now)
    {
        IslandActivity? held = CanUndo(now) ? _held : null;
        _held = null;
        return held;
    }

    /// <summary>Oublie ce qui était gardé (échéance passée, ou autre chose écartée).</summary>
    public void Clear() => _held = null;
}

/// <summary>File d'attente visible (vague 7) : un point par activité qui attend, cinq au plus.</summary>
public static class QueueDots
{
    /// <summary>Points montrés au maximum ; au-delà, le dernier dit « et d'autres ».</summary>
    public const int Max = 5;

    /// <summary>Espacement entre deux points, en DIP.</summary>
    public const double Pitch = 8;

    /// <summary>Côté d'un point, en DIP.</summary>
    public const double Size = 4;

    /// <summary>Nombre de points pour <paramref name="waiting"/> activités en attente.</summary>
    public static int Count(int waiting) => Math.Clamp(waiting, 0, Max);

    /// <summary>Décalage horizontal du point <paramref name="index"/> depuis le centre, en DIP.</summary>
    public static double Offset(int index, int count) => (index - ((count - 1) / 2.0)) * Pitch;
}
