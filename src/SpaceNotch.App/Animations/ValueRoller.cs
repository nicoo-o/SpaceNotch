using System;
using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace SpaceNotch_App.Animations;

/// <summary>
/// Fait défiler une valeur vers sa cible : 63, 64, 65 plutôt qu'un saut.
///
/// <para>
/// Le texte d'un chiffre ne peut pas être animé par le compositeur : il faut
/// le réécrire. Le rouleau s'abonne donc au rendu, <em>seulement</em> le temps
/// du défilement — un sixième de seconde environ — puis se désabonne. Au repos,
/// il ne coûte rien.
/// </para>
/// </summary>
internal sealed class ValueRoller
{
    private readonly Action<double> _apply;
    private readonly TimeSpan _duration;
    private readonly Stopwatch _clock = new();

    private double _from;
    private double _to;
    private double _current = double.NaN;
    private bool _running;

    public ValueRoller(Action<double> apply, TimeSpan duration)
    {
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _duration = duration;
    }

    /// <summary>
    /// Conduit la valeur vers <paramref name="target"/>. Sans animation — ou au
    /// premier appel — elle est posée directement.
    /// </summary>
    public void RollTo(double target, bool animate)
    {
        if (double.IsNaN(_current) || !animate)
        {
            Stop();
            _current = target;
            _apply(target);
            return;
        }

        _from = _current;
        _to = target;
        _clock.Restart();

        if (!_running)
        {
            _running = true;
            CompositionTarget.Rendering += OnFrame;
        }
    }

    public void Stop()
    {
        if (_running)
        {
            CompositionTarget.Rendering -= OnFrame;
            _running = false;
        }

        _clock.Stop();
    }

    private void OnFrame(object? sender, object e)
    {
        double t = _duration.TotalMilliseconds <= 0
            ? 1
            : Math.Clamp(_clock.Elapsed.TotalMilliseconds / _duration.TotalMilliseconds, 0, 1);

        // Décélération : le défilement ralentit en arrivant, sans jamais dépasser.
        double eased = 1 - Math.Pow(1 - t, 3);

        _current = _from + ((_to - _from) * eased);
        _apply(_current);

        if (t >= 1)
        {
            _current = _to;
            Stop();
        }
    }
}
