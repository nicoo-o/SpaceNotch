namespace SpaceNotch.Core.SystemInfo;

/// <summary>
/// Une mesure partagée entre plusieurs lecteurs : la charge du processeur,
/// que lisaient chacun de son côté le moniteur (toutes les 5 s) et Pixel
/// (toutes les 2 s) — deux échantillonneurs pour une même valeur (n° 74).
/// Un lecteur dit jusqu'à quel âge une mesure lui convient ; plus récente, il
/// la reprend, sinon il la refait pour tous.
/// </summary>
public sealed class SharedReading
{
    private readonly Func<double> _measure;
    private readonly Func<long> _milliseconds;
    private readonly object _gate = new();
    private bool _has;
    private double _value;
    private long _at;

    /// <param name="measure">La mesure elle-même (un appel au système).</param>
    /// <param name="milliseconds">Horloge monotone en millisecondes ; <see cref="Environment.TickCount64"/> par défaut.</param>
    public SharedReading(Func<double> measure, Func<long>? milliseconds = null)
    {
        ArgumentNullException.ThrowIfNull(measure);
        _measure = measure;
        _milliseconds = milliseconds ?? (() => Environment.TickCount64);
    }

    /// <summary>
    /// La dernière mesure si elle a moins de <paramref name="maxAge"/>, sinon
    /// une mesure neuve. <see cref="TimeSpan.Zero"/> : toujours neuve.
    /// </summary>
    public double Get(TimeSpan maxAge)
    {
        lock (_gate)
        {
            long now = _milliseconds();

            if (!_has || now - _at >= maxAge.TotalMilliseconds)
            {
                _value = _measure();
                _at = now;
                _has = true;
            }

            return _value;
        }
    }
}
