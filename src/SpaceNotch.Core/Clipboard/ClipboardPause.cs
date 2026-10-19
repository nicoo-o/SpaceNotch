using System;
using System.Threading;

namespace SpaceNotch.Core.Clipboard;

/// <summary>
/// Pause du presse-papier (n° 3, P04 + P25) : un seul état, partagé par l'historique et les
/// actions sur copie. Pendant la pause, ni l'un ni l'autre ne lit le presse-papier ; elle ne
/// touche ni aux réglages ni à l'historique déjà gardé en mémoire, et ne survit pas au
/// redémarrage (un secret copié ne doit pas laisser une pause oubliée).
/// </summary>
public sealed class ClipboardPause : IDisposable
{
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private ITimer? _timer;
    private int _generation;

    public ClipboardPause(TimeProvider? time = null) => _time = time ?? TimeProvider.System;

    /// <summary>Vrai tant que la pause dure.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>L'échéance d'une pause chronométrée ; null pour « jusqu'à la reprise ».</summary>
    public DateTimeOffset? Until { get; private set; }

    /// <summary>Déclenché à chaque changement, depuis n'importe quel fil.</summary>
    public event Action? Changed;

    public void PauseFor(TimeSpan duration)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            int generation = ++_generation;
            Until = _time.GetUtcNow() + duration;
            IsPaused = true;

            // Un rappel déjà parti d'une pause précédente ne doit pas lever celle-ci.
            _timer = _time.CreateTimer(_ => ResumeIf(generation), null, duration, Timeout.InfiniteTimeSpan);
        }

        Changed?.Invoke();
    }

    public void PauseUntilResumed()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            _generation++;
            Until = null;
            IsPaused = true;
        }

        Changed?.Invoke();
    }

    public void Resume() => ResumeIf(null);

    private void ResumeIf(int? generation)
    {
        lock (_gate)
        {
            if (!IsPaused || (generation is { } g && g != _generation))
            {
                return;
            }

            _timer?.Dispose();
            _timer = null;
            Until = null;
            IsPaused = false;
        }

        Changed?.Invoke();
    }

    public void Dispose() => _timer?.Dispose();
}
