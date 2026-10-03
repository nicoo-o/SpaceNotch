using System;
using System.Collections.Generic;
using SpaceNotch.Core.Presentation;

namespace SpaceNotch.Core.Machine;

/// <summary>Présence de la notch à l'écran (RFC §3.2).</summary>
public enum Presence
{
    Visible,

    /// <summary>Retirée : plein écran, session verrouillée. Tout ce qui vit s'arrête.</summary>
    Withdrawn,

    Stopped
}

/// <summary>Où la notch est posée.</summary>
public enum Placement
{
    Attached,
    Tab,
    Floating
}

/// <summary>Ce que fait la main sur la notch.</summary>
public enum HandPhase
{
    Free,
    Pressed,
    Pulling,
    Tearing,
    Dragging
}

/// <summary>Ce que montre la surface. « En mouvement » est l'animateur, pas un état.</summary>
public enum Surface
{
    Rest,
    Preview,
    Open
}

/// <summary>À qui va le clavier.</summary>
public enum KeyboardState
{
    Free,
    Captured
}

/// <summary>Les entrées de la machine.</summary>
public enum NotchTrigger
{
    HoverEnter,
    HoverDwell,
    HoverLeave,
    Press,
    PullMoved,
    Release,
    SecondaryClick,
    HotKey,
    Escape,
    ClickOutside,
    ActivityArrived,
    ActivitiesEmptied,
    PresenceChanged,
    PlacementChanged,
    AnimationSettled
}

/// <summary>Type de pointeur d'un appui.</summary>
public enum PointerKind
{
    Mouse,
    Touch,
    Pen
}

/// <summary>Une entrée et ses détails (RFC §3.4).</summary>
public sealed record NotchInput(
    NotchTrigger Trigger,
    PointerKind Device = PointerKind.Mouse,
    double PullDip = 0,
    double LateralDip = 0,
    double VelocityDip = 0,
    double HeldSeconds = 0,
    bool HasPresented = false,
    bool ClaimsAttention = false,
    Presence? Presence = null,
    Placement? Placement = null);

/// <summary>Ce que la machine sait (une région par champ).</summary>
public sealed record NotchSnapshot(
    Presence Presence,
    Placement Placement,
    HandPhase Hand,
    Surface Surface,
    KeyboardState Keyboard)
{
    public static NotchSnapshot Initial { get; } = new(Presence.Visible, Placement.Attached, HandPhase.Free, Surface.Rest, KeyboardState.Free);
}

/// <summary>Un effet demandé à l'hôte, qui l'interprète.</summary>
public enum NotchEffect
{
    ShowPreview,
    EndPreview,
    Open,
    Close,
    OpenQuickMenu,
    CaptureKeyboard,
    ReleaseKeyboard,
    SuspendLife,
    ResumeLife,
    Tear,
    SpringBack
}

/// <summary>Le résultat d'une entrée : l'état suivant et les effets, dans l'ordre.</summary>
public sealed record NotchStep(NotchSnapshot Next, IReadOnlyList<NotchEffect> Effects);

/// <summary>Réglages qui changent les règles de la machine.</summary>
public sealed record NotchRules(double TearDistance = Detachment.TearDistance, bool AllowDetach = true, bool HoverToPreview = true);

/// <summary>
/// La machine à états de la notch (phase E, RFC §3.2) : pure, sans horloge ni
/// fenêtre. Elle reçoit une entrée, rend l'état suivant et la liste des
/// effets que l'hôte doit jouer. Quatre régions orthogonales — présence,
/// placement et main, surface, clavier — reliées par des gardes :
///
/// <list type="bullet">
/// <item>retirée, la surface est figée, tout ce qui vit s'arrête, le clavier est rendu ;</item>
/// <item>main occupée : pas d'aperçu au survol ;</item>
/// <item>flottante : pas de traction, le clic ouvre aussitôt ;</item>
/// <item>ouverte par l'utilisateur : le clavier est capturé.</item>
/// </list>
///
/// <para>
/// Elle tourne d'abord <b>en ombre</b> : la fenêtre lui donne les mêmes
/// entrées, journalise les écarts avec le comportement réel, et ne la laisse
/// rien piloter (RFC §3.6).
/// </para>
/// </summary>
public sealed class NotchMachine
{
    private NotchRules _rules;

    public NotchMachine(NotchRules? rules = null) => _rules = rules ?? new NotchRules();

    /// <summary>L'état courant.</summary>
    public NotchSnapshot Current { get; private set; } = NotchSnapshot.Initial;

    /// <summary>Change les règles (réglages modifiés).</summary>
    public void UseRules(NotchRules rules) => _rules = rules ?? throw new ArgumentNullException(nameof(rules));

    /// <summary>Repose l'état (réalignement de l'ombre sur le réel).</summary>
    public void Reset(NotchSnapshot state) => Current = state ?? throw new ArgumentNullException(nameof(state));

    /// <summary>Applique une entrée et rend le pas (l'état est mis à jour).</summary>
    public NotchStep Fire(NotchInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        NotchStep step = Next(Current, input, _rules);
        Current = step.Next;
        return step;
    }

    /// <summary>Le pas, sans rien changer : la fonction pure de la machine.</summary>
    public static NotchStep Next(NotchSnapshot state, NotchInput input, NotchRules rules)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(rules);

        var effects = new List<NotchEffect>();
        NotchSnapshot next = state;

        // Garde : retirée ou arrêtée, seule la présence peut changer.
        if (state.Presence != Presence.Visible && input.Trigger != NotchTrigger.PresenceChanged)
        {
            return new NotchStep(state, effects);
        }

        switch (input.Trigger)
        {
            case NotchTrigger.PresenceChanged when input.Presence is { } presence && presence != state.Presence:
                next = next with { Presence = presence };

                if (presence == Presence.Visible)
                {
                    effects.Add(NotchEffect.ResumeLife);
                }
                else
                {
                    effects.Add(NotchEffect.SuspendLife);
                    next = ReleaseKeyboard(next, effects) with { Hand = HandPhase.Free };
                }

                break;

            case NotchTrigger.PlacementChanged when input.Placement is { } placement:
                next = next with { Placement = placement, Hand = HandPhase.Free };
                break;

            case NotchTrigger.HoverDwell
                when rules.HoverToPreview && state.Surface == Surface.Rest && state.Hand == HandPhase.Free:
                next = next with { Surface = Surface.Preview };
                effects.Add(NotchEffect.ShowPreview);
                break;

            case NotchTrigger.HoverLeave when state.Surface == Surface.Preview && state.Hand == HandPhase.Free:
                next = next with { Surface = Surface.Rest };
                effects.Add(NotchEffect.EndPreview);
                break;

            case NotchTrigger.Press when state.Hand == HandPhase.Free:
                next = next with { Hand = HandPhase.Pressed };
                break;

            case NotchTrigger.PullMoved:
                next = Pull(next, input, rules, effects);
                break;

            case NotchTrigger.Release:
                next = Release(next, input, rules, effects);
                break;

            case NotchTrigger.SecondaryClick:
                effects.Add(NotchEffect.OpenQuickMenu);
                break;

            case NotchTrigger.HotKey:
                next = OpenByUser(next with { Hand = HandPhase.Free }, effects, alreadyOpen: state.Surface == Surface.Open);
                break;

            case NotchTrigger.Escape or NotchTrigger.ClickOutside when state.Surface == Surface.Open:
                next = Close(next, effects);
                break;

            case NotchTrigger.ActivityArrived when input.ClaimsAttention && state.Surface != Surface.Open:
                // Une ouverture d'elle-même ne prend jamais le clavier.
                next = next with { Surface = Surface.Open };
                effects.Add(NotchEffect.Open);
                break;

            case NotchTrigger.ActivitiesEmptied when state.Surface == Surface.Open:
                next = Close(next, effects);
                break;
        }

        return new NotchStep(next, effects);
    }

    private static NotchSnapshot Pull(NotchSnapshot state, NotchInput input, NotchRules rules, List<NotchEffect> effects)
    {
        // Garde : flottante, pas de traction — la main déplace la pastille.
        if (state.Placement == Placement.Floating)
        {
            return state.Hand == HandPhase.Pressed && Detachment.ExceedsClickSlop(input.LateralDip, input.PullDip)
                ? state with { Hand = HandPhase.Dragging }
                : state;
        }

        switch (state.Hand)
        {
            case HandPhase.Pressed when Detachment.ExceedsClickSlop(input.LateralDip, input.PullDip):
                return state with { Hand = HandPhase.Pulling };

            case HandPhase.Pulling when rules.AllowDetach && Detachment.ShouldTear(input.PullDip, rules.TearDistance):
                effects.Add(NotchEffect.Tear);
                return state with { Hand = HandPhase.Tearing, Placement = Placement.Floating };

            default:
                return state;
        }
    }

    private static NotchSnapshot Release(NotchSnapshot state, NotchInput input, NotchRules rules, List<NotchEffect> effects)
    {
        NotchSnapshot free = state with { Hand = HandPhase.Free };

        switch (state.Hand)
        {
            case HandPhase.Pressed:
                if (NotchGestures.IsLongPress(input.Device != PointerKind.Mouse, input.HeldSeconds))
                {
                    effects.Add(NotchEffect.OpenQuickMenu);
                    return free;
                }

                return Click(free, input, effects);

            case HandPhase.Pulling:
                if (Detachment.IsClickRelease(input.PullDip, input.LateralDip))
                {
                    return Click(free, input, effects);
                }

                if (state.Surface != Surface.Open
                    && Detachment.OpensOnRelease(input.PullDip, input.VelocityDip, rules.TearDistance, rules.AllowDetach))
                {
                    return OpenByUser(free, effects, alreadyOpen: false);
                }

                effects.Add(NotchEffect.SpringBack);
                return free;

            default:
                return free;
        }
    }

    /// <summary>Un clic ouvre ou referme ; sans rien à présenter, il ouvre la recherche (une ouverture aussi).</summary>
    private static NotchSnapshot Click(NotchSnapshot state, NotchInput input, List<NotchEffect> effects)
        => state.Surface == Surface.Open
            ? Close(state, effects)
            : OpenByUser(state, effects, alreadyOpen: false);

    private static NotchSnapshot OpenByUser(NotchSnapshot state, List<NotchEffect> effects, bool alreadyOpen)
    {
        if (!alreadyOpen)
        {
            effects.Add(NotchEffect.Open);
        }

        // Garde : ouverte par l'utilisateur, la notch prend le clavier.
        if (state.Keyboard != KeyboardState.Captured)
        {
            effects.Add(NotchEffect.CaptureKeyboard);
        }

        return state with { Surface = Surface.Open, Keyboard = KeyboardState.Captured };
    }

    private static NotchSnapshot Close(NotchSnapshot state, List<NotchEffect> effects)
    {
        effects.Add(NotchEffect.Close);
        return ReleaseKeyboard(state with { Surface = Surface.Rest }, effects);
    }

    private static NotchSnapshot ReleaseKeyboard(NotchSnapshot state, List<NotchEffect> effects)
    {
        if (state.Keyboard == KeyboardState.Captured)
        {
            effects.Add(NotchEffect.ReleaseKeyboard);
        }

        return state with { Keyboard = KeyboardState.Free };
    }
}
