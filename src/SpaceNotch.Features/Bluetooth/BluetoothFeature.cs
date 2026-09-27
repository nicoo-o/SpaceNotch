using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Localization;
using SpaceNotch.Platform.Windows.Bluetooth;

namespace SpaceNotch.Features.Bluetooth;

/// <summary>
/// Connexion et déconnexion réelles de périphériques Bluetooth, avec leur
/// batterie quand Windows la connaît.
/// </summary>
public sealed class BluetoothFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Bluetooth;

    private static readonly TimeSpan ConnectedLifetime = TimeSpan.FromSeconds(3);

    /// <summary>Une déconnexion se lit en un coup d'œil : elle part plus vite.</summary>
    private static readonly TimeSpan DisconnectedLifetime = TimeSpan.FromSeconds(2);

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
        _watcher.DeviceChanged += OnDeviceChanged;
        _watcher.Start();

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _watcher.DeviceChanged -= OnDeviceChanged;
        _watcher.Stop();

        return Task.CompletedTask;
    }

    private void OnDeviceChanged(BluetoothDeviceChange change)
    {
        var payload = new BluetoothPayload(change.Name, change.IsConnected, change.BatteryPercent, KindKey(change.Kind));

        string subtitle = !change.IsConnected
            ? Lang.T("Déconnecté", "Disconnected")
            : payload.IsBatteryLow
                ? Lang.T("Batterie faible", "Low battery")
                : Lang.T("Connecté", "Connected");

        // La batterie ne s'écrit plus dans la ligne : elle vit à droite de la
        // forme compacte, en arc et en pourcentage (CompactTrailing).

        // Identifiant dérivé de l'appareil : rebrancher le même casque remplace
        // l'activité précédente au lieu d'en empiler une nouvelle.
        PublishActivity(new IslandActivity
        {
            Id = $"bluetooth.{change.Id}",
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Bluetooth,
            Title = change.Name,
            Subtitle = subtitle,
            Source = "Bluetooth",
            IconKey = payload.IconKey,
            State = IslandActivityState.DeviceActive,
            Priority = ActivityPriority.Normal,
            Duration = change.IsConnected ? ConnectedLifetime : DisconnectedLifetime,
            Payload = payload
        });

        PublishEvent(new BluetoothDeviceChangedEvent(change.Name, change.IsConnected, change.BatteryPercent));
    }

    private static string KindKey(BluetoothDeviceKind kind) => kind switch
    {
        BluetoothDeviceKind.Audio => "audio",
        BluetoothDeviceKind.Keyboard => "keyboard",
        BluetoothDeviceKind.Mouse => "mouse",
        BluetoothDeviceKind.Phone => "phone",
        BluetoothDeviceKind.Gamepad => "gamepad",
        _ => "other"
    };
}
