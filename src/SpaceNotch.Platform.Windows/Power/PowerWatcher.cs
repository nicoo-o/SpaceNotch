using System;
using Windows.System.Power;

namespace SpaceNotch.Platform.Windows.Power;

/// <summary>
/// État de la batterie et du chargeur (F1), par les événements de
/// <see cref="PowerManager"/> — jamais par scrutation. Sans batterie (un
/// ordinateur de bureau), l'événement dit simplement « pas de batterie ».
/// </summary>
public sealed class PowerWatcher : IDisposable
{
    private bool _started;

    /// <summary>Batterie présente ?, chargeur branché ?, niveau en pourcentage.</summary>
    public event Action<bool, bool, int>? Changed;

    public void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;

        try
        {
            PowerManager.BatteryStatusChanged += OnChanged;
            PowerManager.PowerSupplyStatusChanged += OnChanged;
            PowerManager.RemainingChargePercentChanged += OnChanged;
            Report();
        }
        catch (Exception)
        {
            // API indisponible : la fonctionnalité reste muette.
        }
    }

    public void Stop()
    {
        if (!_started)
        {
            return;
        }

        _started = false;

        try
        {
            PowerManager.BatteryStatusChanged -= OnChanged;
            PowerManager.PowerSupplyStatusChanged -= OnChanged;
            PowerManager.RemainingChargePercentChanged -= OnChanged;
        }
        catch (Exception)
        {
            // Rien à défaire.
        }
    }

    public void Dispose() => Stop();

    private void OnChanged(object? sender, object e) => Report();

    private void Report()
    {
        bool hasBattery = PowerManager.BatteryStatus != BatteryStatus.NotPresent;
        bool plugged = PowerManager.PowerSupplyStatus != PowerSupplyStatus.NotPresent;
        Changed?.Invoke(hasBattery, plugged, PowerManager.RemainingChargePercent);
    }
}
