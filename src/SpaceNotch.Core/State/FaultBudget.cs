using System;

namespace SpaceNotch.Core.State;

/// <summary>Ce qu'il faut faire d'une exception non gérée qui vient d'arriver.</summary>
/// <param name="Log">Vrai : l'écrire en entier dans le journal.</param>
/// <param name="Silenced">Exceptions tues depuis la dernière écrite, à mentionner avec elle.</param>
/// <param name="Troubled">
/// Vrai une seule fois par session : les exceptions se répètent assez pour
/// proposer de relancer plutôt que de continuer en silence (n° 71).
/// </param>
public readonly record struct FaultVerdict(bool Log, int Silenced, bool Troubled = false);

/// <summary>
/// Exceptions non gérées (n° 33) : l'application les journalise et continue.
///
/// <para>
/// Une exception d'un minuteur ou d'un gestionnaire <c>async void</c> fermait la
/// notch. Elle continue désormais ; mais un minuteur qui échoue à chaque
/// battement écrirait alors sa pile toutes les secondes. Les premières d'une
/// fenêtre sont écrites en entier, les suivantes seulement comptées, et le
/// compte accompagne la première écrite de la fenêtre suivante.
/// </para>
/// </summary>
public sealed class FaultBudget
{
    /// <summary>Exceptions écrites en entier par fenêtre.</summary>
    public const int LoggedPerWindow = 5;

    /// <summary>Durée d'une fenêtre.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Exceptions dans une même fenêtre à partir desquelles la notch propose de
    /// relancer : une ou deux sont survécues sans bruit ; vingt en une minute
    /// disent qu'une partie d'elle ne marche plus.
    /// </summary>
    public const int TroubledPerWindow = 20;

    private readonly object _lock = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _logged;
    private int _silenced;
    private int _inWindow;
    private bool _troubled;

    /// <summary>Compte une exception survenue à <paramref name="now"/>.</summary>
    public FaultVerdict Record(DateTimeOffset now)
    {
        // Les exceptions arrivent de n'importe quel fil (AppDomain, tâches).
        lock (_lock)
        {
            if (now - _windowStart >= Window)
            {
                _windowStart = now;
                _logged = 0;
                _inWindow = 0;
            }

            _inWindow++;
            bool troubled = !_troubled && _inWindow >= TroubledPerWindow;
            _troubled |= troubled;

            if (_logged < LoggedPerWindow)
            {
                _logged++;
                int silenced = _silenced;
                _silenced = 0;
                return new FaultVerdict(true, silenced, troubled);
            }

            _silenced++;
            return new FaultVerdict(false, 0, troubled);
        }
    }
}
