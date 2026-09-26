using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;

namespace SpaceNotch.Platform.Windows.Bluetooth;

/// <summary>Ce qu'est un appareil Bluetooth, pour choisir son icône.</summary>
public enum BluetoothDeviceKind
{
    Other = 0,
    Audio,
    Keyboard,
    Mouse,
    Phone,
    Gamepad
}

/// <summary>Un appareil qui vient de se connecter ou de se déconnecter.</summary>
/// <param name="Id">Identifiant stable de l'appareil.</param>
/// <param name="Name">Nom affiché.</param>
/// <param name="IsConnected">Nouvel état.</param>
/// <param name="BatteryPercent">Batterie, si Windows la connaît.</param>
/// <param name="Kind">Nature de l'appareil.</param>
public sealed record BluetoothDeviceChange(string Id, string Name, bool IsConnected, int? BatteryPercent, BluetoothDeviceKind Kind);

/// <summary>
/// Connexions et déconnexions réelles des appareils Bluetooth appairés.
///
/// <para>
/// L'ancien écouteur surveillait la liste des appareils : chaque appareil
/// appairé y « arrive » au démarrage, et tous étaient annoncés comme connectés ;
/// les déconnexions, elles, n'étaient jamais vues. Ici, on surveille les points
/// d'association (<c>AssociationEndpoint</c>) avec leur propriété
/// <c>System.Devices.Aep.IsConnected</c> : l'énumération initiale ne fait
/// qu'apprendre l'état de chacun, et seuls les <em>changements</em> ensuite sont
/// annoncés.
/// </para>
/// </summary>
public sealed class BluetoothWatcher : IDisposable
{
    private const string IsConnectedKey = "System.Devices.Aep.IsConnected";
    private const string ContainerIdKey = "System.Devices.Aep.ContainerId";
    private const string MajorClassKey = "System.Devices.Aep.Bluetooth.Cod.Major";
    private const string MinorClassKey = "System.Devices.Aep.Bluetooth.Cod.Minor";

    /// <summary>DEVPKEY_Device_BatteryLevel : la batterie que montre Paramètres › Bluetooth.</summary>
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    /// <summary>Bluetooth classique et Bluetooth basse consommation, appareils appairés seulement.</summary>
    private const string Selector =
        "(System.Devices.Aep.ProtocolId:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\" OR " +
        "System.Devices.Aep.ProtocolId:=\"{bb7bb05e-5972-42b5-94fc-76eaa7084d49}\") AND " +
        "System.Devices.Aep.IsPaired:=System.StructuredQueryType.Boolean#True";

    private static readonly string[] Properties = [IsConnectedKey, ContainerIdKey, MajorClassKey, MinorClassKey];

    private readonly ConcurrentDictionary<string, Known> _devices = new(StringComparer.OrdinalIgnoreCase);

    private DeviceWatcher? _watcher;
    private volatile bool _enumerated;
    private bool _isDisposed;

    public event Action<BluetoothDeviceChange>? DeviceChanged;

    /// <summary>Diagnostic : erreurs de l'API Bluetooth, jamais levées.</summary>
    public Action<string, Exception>? Failed { get; set; }

    public void Start()
    {
        if (_watcher is not null)
        {
            return;
        }

        try
        {
            _enumerated = false;
            _watcher = DeviceInformation.CreateWatcher(Selector, Properties, DeviceInformationKind.AssociationEndpoint);
            _watcher.Added += OnAdded;
            _watcher.Updated += OnUpdated;
            _watcher.Removed += OnRemoved;
            _watcher.EnumerationCompleted += (_, _) => _enumerated = true;
            _watcher.Start();
        }
        catch (Exception ex)
        {
            // Pas d'adaptateur, Bluetooth coupé, service arrêté : rien à surveiller.
            Failed?.Invoke("démarrage", ex);
            _watcher = null;
        }
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        bool connected = ReadBool(info.Properties, IsConnectedKey);
        var known = new Known(
            info.Name,
            connected,
            ReadGuid(info.Properties, ContainerIdKey),
            KindOf(info.Properties));

        _devices[info.Id] = known;

        // Pendant l'énumération initiale, on apprend l'état : rien n'est annoncé.
        // Après, un appareil qui apparaît déjà connecté vient d'être appairé.
        if (_enumerated && connected)
        {
            _ = AnnounceAsync(info.Id, known);
        }
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        if (!_devices.TryGetValue(update.Id, out Known? known)
            || !update.Properties.ContainsKey(IsConnectedKey))
        {
            return;
        }

        bool connected = ReadBool(update.Properties, IsConnectedKey);

        if (connected == known.IsConnected)
        {
            return;
        }

        Known next = known with { IsConnected = connected };
        _devices[update.Id] = next;

        if (_enumerated)
        {
            _ = AnnounceAsync(update.Id, next);
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update) => _devices.TryRemove(update.Id, out _);

    private async Task AnnounceAsync(string id, Known device)
    {
        int? battery = device.IsConnected ? await ReadBatteryAsync(device.ContainerId).ConfigureAwait(false) : null;

        if (string.IsNullOrWhiteSpace(device.Name))
        {
            return;
        }

        DeviceChanged?.Invoke(new BluetoothDeviceChange(id, device.Name, device.IsConnected, battery, device.Kind));
    }

    /// <summary>
    /// La batterie n'est pas sur le point d'association mais sur les nœuds du
    /// même conteneur : on prend le premier qui en publie une. Un appareil qui
    /// vient de se connecter la publie parfois une seconde plus tard ; tant pis,
    /// l'indicateur s'affiche sans.
    /// </summary>
    private async Task<int?> ReadBatteryAsync(Guid? container)
    {
        if (container is not Guid id || id == Guid.Empty)
        {
            return null;
        }

        try
        {
            string query = $"System.Devices.ContainerId:=\"{{{id}}}\"";
            DeviceInformationCollection nodes = await DeviceInformation
                .FindAllAsync(query, [BatteryKey], DeviceInformationKind.Device)
                .AsTask()
                .WaitAsync(TimeSpan.FromSeconds(2))
                .ConfigureAwait(false);

            foreach (DeviceInformation node in nodes)
            {
                if (node.Properties.TryGetValue(BatteryKey, out object? value) && value is byte level && level <= 100)
                {
                    return level;
                }
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke("batterie", ex);
        }

        return null;
    }

    /// <summary>Classe d'appareil Bluetooth (CoD) : 4 audio, 5 périphérique, 2 téléphone.</summary>
    private static BluetoothDeviceKind KindOf(IReadOnlyDictionary<string, object> properties)
    {
        int major = ReadInt(properties, MajorClassKey);
        int minor = ReadInt(properties, MinorClassKey);

        return major switch
        {
            4 => BluetoothDeviceKind.Audio,
            2 => BluetoothDeviceKind.Phone,
            5 when (minor & 0x10) != 0 && (minor & 0x20) == 0 => BluetoothDeviceKind.Keyboard,
            5 when (minor & 0x20) != 0 && (minor & 0x10) == 0 => BluetoothDeviceKind.Mouse,
            5 when (minor & 0x0F) is 1 or 2 => BluetoothDeviceKind.Gamepad,
            _ => BluetoothDeviceKind.Other
        };
    }

    private static bool ReadBool(IReadOnlyDictionary<string, object> properties, string key)
        => properties.TryGetValue(key, out object? value) && value is bool flag && flag;

    private static int ReadInt(IReadOnlyDictionary<string, object> properties, string key)
        => properties.TryGetValue(key, out object? value) ? value switch
        {
            byte b => b,
            ushort u => u,
            uint u => (int)u,
            int i => i,
            _ => 0
        } : 0;

    private static Guid? ReadGuid(IReadOnlyDictionary<string, object> properties, string key)
        => properties.TryGetValue(key, out object? value) && value is Guid guid ? guid : null;

    public void Stop()
    {
        DeviceWatcher? watcher = _watcher;
        _watcher = null;

        if (watcher is null)
        {
            return;
        }

        watcher.Added -= OnAdded;
        watcher.Updated -= OnUpdated;
        watcher.Removed -= OnRemoved;

        try
        {
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }
        catch (Exception ex)
        {
            Failed?.Invoke("arrêt", ex);
        }

        _devices.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        Stop();
    }

    private sealed record Known(string Name, bool IsConnected, Guid? ContainerId, BluetoothDeviceKind Kind);
}
