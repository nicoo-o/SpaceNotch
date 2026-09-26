using System;
using System.Threading;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Startup;

/// <summary>
/// Une seule notch par session.
///
/// <para>
/// L'installeur la lance, Windows la lance à l'ouverture de session, le
/// raccourci du bureau et le menu Démarrer la lancent : sans garde, chaque
/// lancement ajoutait une notch superposée, une icône de notification, un
/// historique de presse-papier — et deux processus qui écrivaient la même
/// configuration. Le premier processus réclame un mutex nommé ; les suivants
/// signalent un événement nommé, que le premier écoute pour se montrer, puis
/// se retirent.
/// </para>
/// </summary>
internal sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SpaceNotch.Island";
    private const string RevealEventName = @"Local\SpaceNotch.Island.Reveal";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle _reveal;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, EventWaitHandle reveal)
    {
        _mutex = mutex;
        _reveal = reveal;
    }

    /// <summary>
    /// Réclame la place. Renvoie <c>null</c> si une notch tourne déjà : elle a
    /// été prévenue, le processus appelant doit se retirer.
    /// </summary>
    public static SingleInstance? TryClaim()
    {
        var mutex = new Mutex(initiallyOwned: true, MutexName, out bool first);
        var reveal = new EventWaitHandle(false, EventResetMode.AutoReset, RevealEventName);

        if (first)
        {
            return new SingleInstance(mutex, reveal);
        }

        MiniLogger.Log("Une notch tourne déjà : elle est réveillée, ce lancement se retire.");
        reveal.Set();
        reveal.Dispose();
        mutex.Dispose();
        return null;
    }

    /// <summary>Appelle <paramref name="onReveal"/> à chaque second lancement, sur un fil du pool.</summary>
    public void ListenForReveal(Action onReveal)
    {
        ArgumentNullException.ThrowIfNull(onReveal);

        _registration = ThreadPool.RegisterWaitForSingleObject(
            _reveal,
            (_, _) => onReveal(),
            state: null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _reveal.Dispose();

        try
        {
            _mutex.ReleaseMutex();
        }
        catch (ApplicationException)
        {
            // Libéré depuis un autre fil : le système le relâchera à la sortie.
        }

        _mutex.Dispose();
    }
}
