using System;
using SpaceNotch.Core.State;

namespace SpaceNotch.Core.Machine;

/// <summary>
/// La machine en ombre (RFC §3.6) : elle reçoit les mêmes entrées que la
/// notch réelle, ne pilote rien, et compare sa surface à l'état réel quand
/// celui-ci se pose. Les écarts sont comptés et décrits pour le journal :
/// ce sont eux qui diront, région par région, quand la machine peut prendre
/// la main.
/// </summary>
public sealed class NotchShadow
{
    private readonly NotchMachine _machine;

    public NotchShadow(NotchRules? rules = null) => _machine = new NotchMachine(rules);

    /// <summary>L'état de la machine.</summary>
    public NotchSnapshot Current => _machine.Current;

    /// <summary>Entrées reçues.</summary>
    public long Inputs { get; private set; }

    /// <summary>Comparaisons faites.</summary>
    public long Checks { get; private set; }

    /// <summary>Écarts constatés.</summary>
    public long Divergences { get; private set; }

    /// <summary>Change les règles (réglages modifiés).</summary>
    public void UseRules(NotchRules rules) => _machine.UseRules(rules);

    /// <summary>Donne une entrée à la machine.</summary>
    public NotchStep Feed(NotchInput input)
    {
        Inputs++;
        return _machine.Fire(input);
    }

    /// <summary>La surface que l'état réel de l'Island représente.</summary>
    public static Surface SurfaceOf(IslandState state) => state switch
    {
        IslandState.Preview => Surface.Preview,
        IslandState.Expanding or IslandState.Expanded => Surface.Open,
        _ => Surface.Rest
    };

    /// <summary>
    /// Compare la surface de la machine à l'état réel, une fois celui-ci posé.
    /// Rend la description de l'écart, ou <c>null</c> s'ils s'accordent.
    /// </summary>
    public string? Compare(IslandState actual, string context)
    {
        Checks++;
        Surface real = SurfaceOf(actual);

        if (real == _machine.Current.Surface)
        {
            return null;
        }

        Divergences++;
        return $"[OMBRE] écart ({context}) : réel {real} ({actual}), machine {_machine.Current.Surface} — {_machine.Current}";
    }

    /// <summary>
    /// Réaligne la machine sur l'état réel après un écart : un écart ne doit
    /// pas en entraîner d'autres en cascade, chacun doit être compté une fois.
    /// </summary>
    public void Resync(IslandState actual)
    {
        Surface real = SurfaceOf(actual);
        NotchSnapshot current = _machine.Current;

        if (current.Surface == real)
        {
            return;
        }

        _machine.Reset(current with
        {
            Surface = real,
            Keyboard = real == Surface.Open ? current.Keyboard : KeyboardState.Free
        });
    }

    /// <summary>Résumé pour le journal.</summary>
    public string Summary => $"[OMBRE] {Inputs} entrée(s), {Checks} comparaison(s), {Divergences} écart(s)";
}
