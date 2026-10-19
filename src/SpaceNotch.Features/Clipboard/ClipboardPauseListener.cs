using System;
using SpaceNotch.Core.Clipboard;
using SpaceNotch.Platform.Windows.Clipboard;

namespace SpaceNotch.Features.Clipboard;

/// <summary>
/// Abonne un lecteur du presse-papier tant que la pause n'est pas active : en pause, le
/// gestionnaire est réellement retiré du moniteur, donc rien n'est lu (n° 3, P25).
/// </summary>
public static class ClipboardPauseListener
{
    public static IDisposable Attach(ClipboardMonitor monitor, Action handler, ClipboardPause pause)
    {
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(pause);

        return new Subscription(monitor, handler, pause);
    }

    private sealed class Subscription : IDisposable
    {
        private readonly ClipboardMonitor _monitor;
        private readonly Action _handler;
        private readonly ClipboardPause _pause;
        private readonly object _gate = new();
        private bool _attached;
        private bool _disposed;

        public Subscription(ClipboardMonitor monitor, Action handler, ClipboardPause pause)
        {
            _monitor = monitor;
            _handler = handler;
            _pause = pause;
            _pause.Changed += Sync;
            Sync();
        }

        private void Sync()
        {
            lock (_gate)
            {
                bool wanted = !_disposed && !_pause.IsPaused;

                if (wanted == _attached)
                {
                    return;
                }

                if (wanted)
                {
                    _monitor.ClipboardUpdated += _handler;
                }
                else
                {
                    _monitor.ClipboardUpdated -= _handler;
                }

                _attached = wanted;
            }
        }

        public void Dispose()
        {
            _pause.Changed -= Sync;
            _disposed = true;
            Sync();
        }
    }
}
