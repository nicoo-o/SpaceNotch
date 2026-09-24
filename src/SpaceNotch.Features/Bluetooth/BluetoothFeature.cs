using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Platform.Windows.Bluetooth;

namespace SpaceNotch.Features.Bluetooth;

/// <summary>
/// Connexion et déconnexion de périphériques Bluetooth.
/// </summary>
public sealed class BluetoothFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Bluetooth;

    private static readonly TimeSpan NotificationLifetime = TimeSpan.FromSeconds(3);

    private readonly BluetoothWatcher _watcher;

    public BluetoothFeature(
        IActivityManager activities,
        IEventBus events,
        BluetoothWatcher watcher,
        bool isEnabled = true)
        : base(FeatureKey, "Bluetooth", activities, events, isEnabled)
    {
        _watcher = watcher ?? throw new ArgumentNullException(nameof(watcher));
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _watcher.DeviceStatusChanged += OnDeviceStatusChanged;
        _watcher.Start();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _watcher.DeviceStatusChanged -= OnDeviceStatusChanged;
        _watcher.Stop();

        return Task.CompletedTask;
    }

    private void OnDeviceStatusChanged(string deviceName, bool isConnected, int? batteryPercent)
    {
        string subtitle = isConnected
            ? batteryPercent.HasValue
                ? $"Connecté · Batterie {batteryPercent}%"
                : "Connecté"
            : "Déconnecté";

        // Identifiant dérivé du nom du périphérique : rebrancher le même casque
        // remplace l'activité précédente au lieu d'en créer une nouvelle à chaque
        // fois. La durée de vie garantit sa disparition.
        var activity = new IslandActivity
        {
            Id = $"bluetooth.{deviceName}",
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Bluetooth,
            Title = deviceName,
            Subtitle = subtitle,
            Source = "Bluetooth",
            IconKey = "Bluetooth",
            State = IslandActivityState.DeviceActive,
            Priority = ActivityPriority.Normal,
            Duration = NotificationLifetime
        };

        PublishActivity(activity);
        PublishEvent(new BluetoothDeviceChangedEvent(deviceName, isConnected, batteryPercent));
    }
}
