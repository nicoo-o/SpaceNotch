using System;

namespace SpaceNotch.Core.State;

/// <summary>Ce qu'il faut faire d'une exception non gérée qui vient d'arriver.</summary>
/// <param name="Log">Vrai : l'écrire en entier dans le journal.</param>
/// <param name="Silenced">Exceptions tues depuis la dernière écrite, à mentionner avec elle.</param>
public readonly record struct FaultVerdict(bool Log, int Silenced);

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

    private readonly object _lock = new();
    private DateTimeOffset _windowStart = DateTimeOffset.MinValue;
    private int _logged;
    private int _silenced;

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
            }

            if (_logged < LoggedPerWindow)
            {
                _logged++;
                int silenced = _silenced;
                _silenced = 0;
                return new FaultVerdict(true, silenced);
            }

            _silenced++;
            return new FaultVerdict(false, 0);
        }
    }
}
