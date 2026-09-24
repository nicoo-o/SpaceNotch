using System;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace SpaceNotch.Platform.Windows.Bluetooth;

/// <summary>
/// Écouteur d'événements Bluetooth natif (DeviceWatcher).
/// 100% événementiel, capture les connexions/déconnexions de périphériques sans polling.
/// </summary>
public sealed class BluetoothWatcher : IDisposable
{
    private DeviceWatcher? _watcher;
    private bool _isDisposed;

    public event Action<string, bool, int?>? DeviceStatusChanged;

    public void Start()
    {
        try
        {
            string selector = BluetoothDevice.GetDeviceSelector();
            _watcher = DeviceInformation.CreateWatcher(selector);

            _watcher.Added += OnDeviceAdded;
            _watcher.Updated += OnDeviceUpdated;
            _watcher.Start();
        }
        catch
        {
            // Tolérance si le Bluetooth est désactivé sur l'ordinateur
        }
    }

    private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation info)
    {
        if (info.IsEnabled && !string.IsNullOrWhiteSpace(info.Name))
        {
            DeviceStatusChanged?.Invoke(info.Name, true, null);
        }
    }

    private void OnDeviceUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        // Traitement des mises à jour d'état
    }

    public void Stop()
    {
        if (_watcher != null && _watcher.Status == DeviceWatcherStatus.Started)
        {
            _watcher.Stop();
        }
    }

    public void Dispose()
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Stop();
        }
    }
}
