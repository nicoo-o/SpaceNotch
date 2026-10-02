using System;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.Core.Motion;

/// <summary>Où se posent les yeux dans ce qui arrive.</summary>
public enum HandoffAnchor
{
    /// <summary>Le glyphe 7 × 7 de la pastille : coordonnées en cases, depuis son coin haut-gauche.</summary>
    Glyph,

    /// <summary>Clawd : coordonnées en cases de son dessin recadré (24 × 17).</summary>
    Clawd,

    /// <summary>L'élément de fin de ligne (égaliseur, anneau) : décalage en DIP depuis son centre.</summary>
    Trailing,

    /// <summary>Les deux-points du titre (« 07:42 ») : décalage en DIP depuis leur centre.</summary>
    Colon,

    /// <summary>Le début du titre : les yeux le lisent jusqu'au bout.</summary>
    Title,

    /// <summary>Les contrôles de la carte, dans l'ordre : chaque œil devient un bouton.</summary>
    Actions,

    /// <summary>Le champ de saisie d'une scène ouverte (recherche, note) : décalage en DIP depuis son début.</summary>
    Field
}

/// <summary>Ce que font les yeux avant de partir.</summary>
public enum HandoffLead
{
    None,

    /// <summary>Ils baissent le regard.</summary>
    LookDown,

    /// <summary>Ils lèvent le regard.</summary>
    LookUp,

    /// <summary>En haut à droite : il réfléchit.</summary>
    LookUpRight,

    /// <summary>À droite, vers ce qui arrive.</summary>
    LookRight,

    /// <summary>Yeux ronds et tremblement, comme le téléphone.</summary>
    Shake,

    /// <summary>Deux clignements très rapides, comme un obturateur, puis un éclair.</summary>
    Shutter,

    /// <summary>Ils se ferment doucement, comme la nuit.</summary>
    Close,

    /// <summary>Ils se remplissent de la teinte, du bas vers le haut.</summary>
    Fill,

    /// <summary>Ils se plissent en biais et rougissent : inquiets.</summary>
    Worry,

    /// <summary>Ils prennent la teinte de l'activité avant de partir.</summary>
    Tint,

    /// <summary>Un arceau se dessine au-dessus d'eux (le casque).</summary>
    Arc,

    /// <summary>Ils s'écartent en tendant une barre entre eux.</summary>
    Stretch
}

/// <summary>Où va un œil : centre et taille, dans l'unité de l'ancre ; rondeur de 0 (carré) à 1 (cercle).</summary>
public readonly record struct HandoffSpot(double X, double Y, double Width, double Height, double Roundness = 0);

/// <summary>Ce qui se passe une fois les yeux arrivés.</summary>
public enum HandoffAfter
{
    /// <summary>Ils se fondent dans ce qui s'allume autour d'eux.</summary>
    Merge,

    /// <summary>Ils restent sombres : les yeux de Clawd.</summary>
    Holes,

    /// <summary>Ils dansent : les barres de l'égaliseur.</summary>
    Dance,

    /// <summary>L'œil droit fait le tour de l'anneau et le trace.</summary>
    Orbit,

    /// <summary>Ils lisent le titre de gauche à droite.</summary>
    Read,

    /// <summary>Ils restent et clignotent : le curseur de saisie.</summary>
    Caret
}

/// <summary>
/// Une recette de passage des yeux de Pixel à une fonction (et retour) : où
/// ils vont, ce qu'ils font avant, ce qu'ils deviennent. Les 21 recettes ont
/// été validées une à une sur maquette ; la fenêtre ne fait que les jouer.
/// </summary>
public sealed record HandoffRecipe(
    string Key,
    HandoffLead Lead,
    HandoffAnchor Anchor,
    HandoffSpot Left,
    HandoffSpot Right,
    HandoffAfter After = HandoffAfter.Merge,
    string? Tint = null);

/// <summary>Le choix de la recette, à partir de ce que l'activité déclare.</summary>
public static class PixelHandoff
{
    /// <summary>Durée du voyage des yeux vers leur place.</summary>
    public const int TravelMilliseconds = 420;

    /// <summary>Durée du retour, plus court : on attend l'information, pas son départ.</summary>
    public const int ReturnMilliseconds = 380;

    /// <summary>Durée du geste d'avant (regard, clignement, remplissage).</summary>
    public static int LeadMilliseconds(HandoffLead lead) => lead switch
    {
        HandoffLead.None => 0,
        HandoffLead.Shake => 480,
        HandoffLead.Shutter => 360,
        HandoffLead.Close or HandoffLead.Fill => 520,
        HandoffLead.Arc => 460,
        HandoffLead.Worry or HandoffLead.Tint => 320,
        HandoffLead.Stretch => 360,
        _ => 240
    };

    /// <summary>Teinte qui vient de l'activité (sa couleur), plutôt que d'une couleur fixe.</summary>
    public const string ActivityTint = "activity";

    // Les deux yeux posés de part et d'autre du centre du glyphe, par défaut.
    private static readonly HandoffRecipe Fallback = new("glyph", HandoffLead.None, HandoffAnchor.Glyph, new(2, 3, 1, 1), new(4, 3, 1, 1), Tint: ActivityTint);

    /// <summary>La recette d'une activité ; une activité inconnue reçoit un passage simple vers son glyphe.</summary>
    public static HandoffRecipe For(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        string icon = activity.IconKey ?? string.Empty;

        // Les charges utiles d'abord : elles disent exactement ce qui est montré.
        switch (activity.Payload)
        {
            case ClawdPayload:
                // Les trous des yeux de Clawd, recadré : colonnes 8-9 et 15-16, rangées 8-9.
                return new("clawd", HandoffLead.None, HandoffAnchor.Clawd, new(9, 9, 2, 2), new(16, 9, 2, 2), HandoffAfter.Holes, "#000000");
            case ProgressStepsPayload:
                return new("progress", HandoffLead.Stretch, HandoffAnchor.Glyph, new(1, 3, 1.6, 1.6), new(5, 3, 1.6, 1.6), Tint: ActivityTint);
            case DeliveryPayload:
                return new("delivery", HandoffLead.LookDown, HandoffAnchor.Glyph, new(1, 6, 1.8, 1.8, 1), new(5, 6, 1.8, 1.8, 1), Tint: ActivityTint);
            case VoicePayload:
                return new("voice", HandoffLead.None, HandoffAnchor.Glyph, new(1.5, 3, 3, 3), new(5.5, 3, 3, 3), Tint: ActivityTint);
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Media, StringComparison.Ordinal))
        {
            return Music;
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Launcher, StringComparison.Ordinal))
        {
            // L'œil gauche s'arrondit en loupe, le droit devient le curseur.
            return new("search", HandoffLead.None, HandoffAnchor.Field, new(-14, 0, 11, 11, 1), new(1, 0, 1.4, 15), HandoffAfter.Caret, "#EBFFFFFF");
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Note, StringComparison.Ordinal))
        {
            return new("note", HandoffLead.LookDown, HandoffAnchor.Field, new(-6, -10, 3, 3, 1), new(1, 0, 1.4, 15), HandoffAfter.Caret, "#EBFFFFFF");
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Color, StringComparison.Ordinal) || icon == "Palette")
        {
            // Les yeux prennent la couleur, puis fondent en une pastille.
            return new("color", HandoffLead.Tint, HandoffAnchor.Glyph, new(2.5, 3, 3.5, 7, 0.4), new(4.5, 3, 3.5, 7, 0.4), Tint: ActivityTint);
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Share, StringComparison.Ordinal) || icon == "Qr")
        {
            return new("qr", HandoffLead.None, HandoffAnchor.Glyph, new(1, 1, 3, 3), new(5, 1, 3, 3), Tint: "#EBFFFFFF");
        }

        if (string.Equals(activity.SceneKey, IslandSceneCatalog.Timer, StringComparison.Ordinal)
            || string.Equals(activity.SceneKey, IslandSceneCatalog.Pomodoro, StringComparison.Ordinal)
            || icon == "Timer")
        {
            // Les deux-points : deux points de 1,8, l'un au-dessus de l'autre.
            return new("timer", HandoffLead.None, HandoffAnchor.Colon, new(0, -2.2, 1.8, 1.8), new(0, 2.2, 1.8, 1.8), Tint: "#EBFFFFFF");
        }

        return icon switch
        {
            "Music" or "Play" or "Pause" => Music,
            "Volume" or "VolumeHigh" or "VolumeMedium" or "VolumeLow" or "VolumeMute" or "Brightness"
                => new("volume", HandoffLead.LookUp, HandoffAnchor.Glyph, new(4, 3, 1, 5), new(6, 3, 1, 7), Tint: ActivityTint),
            "Notification" or "Message" => new("notification", HandoffLead.None, HandoffAnchor.Title, new(0, 0, 3, 4), new(5, 0, 3, 4), HandoffAfter.Read),
            "Download" => new("download", HandoffLead.LookDown, HandoffAnchor.Glyph, new(1, 3, 1.6, 1.6), new(5, 3, 1.6, 1.6), Tint: ActivityTint),
            "Headphones" or "Bluetooth" => new("headphones", HandoffLead.Arc, HandoffAnchor.Glyph, new(0.5, 4, 2, 3), new(5.5, 4, 2, 3), Tint: ActivityTint),
            "Agent" or "Progress" or "Info" when activity.MotionState == ActivityMotionState.Working
                => new("work", HandoffLead.LookUpRight, HandoffAnchor.Glyph, new(3, 1, 1.6, 1.6), new(5, 3, 1.6, 1.6), Tint: ActivityTint),
            "Call" => new("call", HandoffLead.Shake, HandoffAnchor.Actions, new(0, 0, 0, 0, 1), new(1, 0, 0, 0, 1), Tint: ActivityTint),
            "Scooter" or "Car" or "Parcel" => new("delivery", HandoffLead.LookDown, HandoffAnchor.Glyph, new(1, 6, 1.8, 1.8, 1), new(5, 6, 1.8, 1.8, 1), Tint: ActivityTint),
            "Calendar" => new("meeting", HandoffLead.LookRight, HandoffAnchor.Glyph, new(4, 5, 1, 1), new(0, 0, 0, 0), HandoffAfter.Orbit, ActivityTint),
            "Bolt" or "Battery" => new("charge", HandoffLead.Fill, HandoffAnchor.Glyph, new(2.5, 3, 1, 3), new(3.5, 3, 1, 3), Tint: ActivityTint),
            "Moon" => new("quiet", HandoffLead.Close, HandoffAnchor.Glyph, new(1, 5, 1, 1), new(2, 6, 1, 1), Tint: ActivityTint),
            "Text" or "Clipboard" => new("copy", HandoffLead.Shutter, HandoffAnchor.Glyph, new(3, 3, 0.5, 0.5), new(3, 3, 0.5, 0.5), Tint: "#EBFFFFFF"),
            "Cpu" => new("monitor", HandoffLead.Worry, HandoffAnchor.Glyph, new(2, 3, 1, 1), new(4, 3, 1, 1), Tint: ActivityTint),
            _ => Fallback
        };
    }

    private static readonly HandoffRecipe Music = new(
        "music",
        HandoffLead.None,
        HandoffAnchor.Trailing,
        new(-4, 0, 2, 8, 0.5),
        new(0, 0, 2, 8, 0.5),
        HandoffAfter.Dance,
        ActivityTint);
}
