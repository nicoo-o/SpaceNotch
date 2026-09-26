namespace SpaceNotch.Core.Productivity;

/// <summary>
/// Une mesure de temps — compte à rebours ou chronomètre — calculée depuis une
/// échéance plutôt que comptée seconde par seconde.
///
/// Compter les battements d'un minuteur dérivait (un battement en retard est
/// une seconde perdue) et s'arrêtait pendant la veille : un minuteur de 25 min
/// lancé avant de fermer le capot en affichait encore 20 au réveil. Ici, le
/// battement ne sert qu'à redessiner ; la valeur vient toujours de l'horloge.
/// </summary>
public sealed class MeasureClock
{
    private readonly Func<DateTimeOffset> _now;

    private TimeSpan _frozen;
    private DateTimeOffset _anchor;

    public MeasureClock(Func<DateTimeOffset>? now = null)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>Vrai pour un compte à rebours, faux pour un chronomètre.</summary>
    public bool CountsDown { get; private set; } = true;

    public bool IsRunning { get; private set; }

    /// <summary>
    /// Valeur courante : le temps restant (jamais négatif) pour un compte à
    /// rebours, le temps écoulé pour un chronomètre. Arrondie à la seconde
    /// supérieure pour un rebours : « 00:01 » s'affiche jusqu'au bout, et
    /// « 00:00 » veut dire fini.
    /// </summary>
    public TimeSpan Value
    {
        get
        {
            if (!IsRunning)
            {
                return _frozen;
            }

            if (!CountsDown)
            {
                return Floor(_now() - _anchor);
            }

            TimeSpan left = _anchor - _now();
            return left <= TimeSpan.Zero ? TimeSpan.Zero : Ceiling(left);
        }
    }

    /// <summary>Vrai quand un compte à rebours en marche est arrivé à zéro.</summary>
    public bool IsFinished => IsRunning && CountsDown && _anchor <= _now();

    /// <summary>Prépare une mesure arrêtée, sans la lancer.</summary>
    public void Set(TimeSpan value, bool countsDown)
    {
        CountsDown = countsDown;
        IsRunning = false;
        _frozen = value < TimeSpan.Zero ? TimeSpan.Zero : value;
    }

    /// <summary>Lance (ou reprend) la mesure depuis sa valeur arrêtée.</summary>
    public void Start()
    {
        if (IsRunning)
        {
            return;
        }

        DateTimeOffset now = _now();
        _anchor = CountsDown ? now + _frozen : now - _frozen;
        IsRunning = true;
    }

    /// <summary>Suspend la mesure : la valeur est figée là où elle en est.</summary>
    public void Pause()
    {
        if (!IsRunning)
        {
            return;
        }

        _frozen = Value;
        IsRunning = false;
    }

    private static TimeSpan Ceiling(TimeSpan value)
        => TimeSpan.FromSeconds(Math.Ceiling(value.TotalSeconds - 1e-6));

    private static TimeSpan Floor(TimeSpan value)
        => TimeSpan.FromSeconds(Math.Floor(value.TotalSeconds + 1e-6));
}
