using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace SpaceNotch.Platform.Windows.Privacy;

/// <summary>
/// Micro et caméra en cours d'utilisation, lus là où Windows tient son propre
/// indicateur de confidentialité : le magasin de consentement
/// <c>CapabilityAccessManager\ConsentStore</c>.
///
/// <para>
/// Chaque application qui a utilisé un capteur y a une clé, avec l'heure du
/// début et de la fin de son dernier usage. Une fin à zéro signifie « en
/// cours » : c'est exactement ce que lit l'icône de micro de la barre des
/// tâches. Rien n'est scruté : <c>RegNotifyChangeKeyValue</c> réveille un fil
/// dédié à chaque modification, et le fil dort le reste du temps.
/// </para>
/// </summary>
public sealed partial class CapabilityUsageWatcher : ICapabilityUsageSource
{
    private const string ConsentStore = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore";

    private const string NonPackaged = "NonPackaged";

    private const uint NotifyChangeName = 0x1;
    private const uint NotifyChangeLastSet = 0x4;

    /// <summary>Regroupe les rafales d'écritures d'un même changement en un seul signal.</summary>
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(150);

    private static readonly (CapabilityKind Kind, string Key)[] Capabilities =
    [
        (CapabilityKind.Microphone, "microphone"),
        (CapabilityKind.Camera, "webcam")
    ];

    private readonly object _gate = new();
    private Thread? _thread;
    private ManualResetEvent? _stop;
    private bool _disposed;

    public event EventHandler? Changed;

    public void StartWatching()
    {
        lock (_gate)
        {
            if (_thread is not null || _disposed)
            {
                return;
            }

            _stop = new ManualResetEvent(false);
            _thread = new Thread(Watch)
            {
                IsBackground = true,
                Name = "SpaceNotch.CapabilityUsageWatcher",
                Priority = ThreadPriority.BelowNormal
            };
            _thread.Start(_stop);
        }
    }

    public void StopWatching()
    {
        Thread? thread;
        ManualResetEvent? stop;

        lock (_gate)
        {
            thread = _thread;
            stop = _stop;
            _thread = null;
            _stop = null;
        }

        if (thread is null || stop is null)
        {
            return;
        }

        stop.Set();
        thread.Join(TimeSpan.FromSeconds(1));
        stop.Dispose();
    }

    public IReadOnlyList<CapabilityUsage> Snapshot()
    {
        var usages = new List<CapabilityUsage>();

        foreach ((CapabilityKind kind, string key) in Capabilities)
        {
            using RegistryKey? store = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{key}");

            if (store is null)
            {
                continue;
            }

            foreach (string app in store.GetSubKeyNames())
            {
                if (string.Equals(app, NonPackaged, StringComparison.OrdinalIgnoreCase))
                {
                    using RegistryKey? desktop = store.OpenSubKey(app);

                    if (desktop is null)
                    {
                        continue;
                    }

                    foreach (string exe in desktop.GetSubKeyNames())
                    {
                        using RegistryKey? entry = desktop.OpenSubKey(exe);

                        if (InUse(entry))
                        {
                            usages.Add(new CapabilityUsage(kind, exe));
                        }
                    }

                    continue;
                }

                using RegistryKey? packaged = store.OpenSubKey(app);

                if (InUse(packaged))
                {
                    usages.Add(new CapabilityUsage(kind, app));
                }
            }
        }

        return usages;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        StopWatching();
        _disposed = true;
        Changed = null;
    }

    /// <summary>Un usage a commencé et n'a pas encore fini.</summary>
    private static bool InUse(RegistryKey? entry)
    {
        if (entry is null)
        {
            return false;
        }

        long start = ReadQword(entry, "LastUsedTimeStart");
        long stop = ReadQword(entry, "LastUsedTimeStop");

        return start > 0 && stop == 0;
    }

    private static long ReadQword(RegistryKey key, string name)
        => key.GetValue(name) switch
        {
            long value => value,
            int value => value,
            _ => -1
        };

    /// <summary>
    /// Fil de veille : il arme une notification par capteur, dort jusqu'à ce
    /// que l'une se déclenche ou qu'on l'arrête, puis réarme. Les notifications
    /// asynchrones de registre sont liées au fil qui les a demandées : c'est
    /// pourquoi un fil dédié, et non le pool, les porte.
    /// </summary>
    private void Watch(object? state)
    {
        var stop = (ManualResetEvent)state!;
        var keys = new List<RegistryKey>();
        var events = new List<AutoResetEvent>();

        try
        {
            foreach ((_, string key) in Capabilities)
            {
                RegistryKey? store = Registry.CurrentUser.OpenSubKey($@"{ConsentStore}\{key}");

                if (store is null)
                {
                    continue;
                }

                keys.Add(store);
                events.Add(new AutoResetEvent(false));
            }

            if (keys.Count == 0)
            {
                return;
            }

            var handles = new WaitHandle[events.Count + 1];
            handles[0] = stop;

            for (int i = 0; i < events.Count; i++)
            {
                handles[i + 1] = events[i];
                Arm(keys[i], events[i]);
            }

            while (true)
            {
                int signaled = WaitHandle.WaitAny(handles);

                if (signaled == 0)
                {
                    return;
                }

                Arm(keys[signaled - 1], events[signaled - 1]);

                // Une application qui prend le micro écrit plusieurs valeurs
                // d'affilée : on laisse la rafale passer avant de relire.
                if (stop.WaitOne(Debounce))
                {
                    return;
                }

                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception)
        {
            // Un magasin illisible ne doit rien casser : la fonctionnalité reste
            // simplement muette.
        }
        finally
        {
            foreach (RegistryKey key in keys)
            {
                key.Dispose();
            }

            foreach (AutoResetEvent handle in events)
            {
                handle.Dispose();
            }
        }
    }

    private static void Arm(RegistryKey key, AutoResetEvent signal)
        => _ = RegNotifyChangeKeyValue(
            key.Handle,
            watchSubtree: true,
            NotifyChangeName | NotifyChangeLastSet,
            signal.SafeWaitHandle,
            asynchronous: true);

    [LibraryImport("advapi32.dll")]
    private static partial int RegNotifyChangeKeyValue(
        SafeRegistryHandle key,
        [MarshalAs(UnmanagedType.Bool)] bool watchSubtree,
        uint notifyFilter,
        SafeWaitHandle signal,
        [MarshalAs(UnmanagedType.Bool)] bool asynchronous);
}
