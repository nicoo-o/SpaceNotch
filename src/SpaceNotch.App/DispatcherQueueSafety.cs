using Microsoft.UI.Dispatching;
using SpaceNotch_App.Diagnostics;

namespace SpaceNotch_App;

/// <summary>
/// <c>TryEnqueue</c> gardé : une exception du travail posté arrêtait le processus
/// sans passer par <c>Application.UnhandledException</c> (n° 33, voir
/// <see cref="Guard"/>). Dans l'espace de noms racine pour servir toute
/// l'application sans <c>using</c> ; un <c>TryEnqueue</c> nu ne devrait plus
/// apparaître dans <c>SpaceNotch.App</c>, hormis l'essai <c>--fault-test</c>.
/// </summary>
/// <remarks>
/// Les appels nus sont interdits par <c>BannedSymbols.txt</c> (n° 70) : seule
/// cette classe y touche.
/// </remarks>
#pragma warning disable RS0030 // Les appels gardés eux-mêmes.
internal static class DispatcherQueueSafety
{
    /// <summary>Poste un travail gardé sur le fil de cette file.</summary>
    public static bool TryEnqueueSafely(this DispatcherQueue queue, DispatcherQueueHandler work)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return queue.TryEnqueue(Guard.Run(work));
    }

    /// <summary>Poste un travail gardé sur le fil de cette file, à cette priorité.</summary>
    public static bool TryEnqueueSafely(this DispatcherQueue queue, DispatcherQueuePriority priority, DispatcherQueueHandler work)
    {
        ArgumentNullException.ThrowIfNull(queue);
        return queue.TryEnqueue(priority, Guard.Run(work));
    }

    /// <summary>Abonne un battement gardé : une exception arrête ce minuteur et la notch continue.</summary>
    public static void OnTick(this DispatcherQueueTimer timer, global::Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object> tick)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Tick += Guard.Tick(tick);
    }

    /// <summary>Abonne un battement de <c>DispatcherTimer</c> XAML gardé.</summary>
    public static void OnTick(this Microsoft.UI.Xaml.DispatcherTimer timer, EventHandler<object> tick)
    {
        ArgumentNullException.ThrowIfNull(timer);
        timer.Tick += Guard.XamlTick(tick);
    }
}
#pragma warning restore RS0030
