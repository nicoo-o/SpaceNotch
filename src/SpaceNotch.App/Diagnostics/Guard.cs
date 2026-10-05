using Microsoft.UI.Dispatching;
using SpaceNotch.Core.State;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Diagnostics;

/// <summary>
/// Garde des rappels que Windows appelle hors de XAML : battement d'un
/// <see cref="DispatcherQueueTimer"/>, travail posté par <c>TryEnqueue</c>.
///
/// <para>
/// Une exception levée là ne passe pas par <c>Application.UnhandledException</c> :
/// CoreMessaging la range en « exception stockée » et arrête le processus
/// (0xc000027b dans CoreMessagingXP.dll, essai <c>--fault-test</c> du
/// 2026-10-05). C'est ainsi qu'un minuteur fermait la notch (n° 33). Le rappel
/// gardé journalise l'exception — sans noyer le journal, voir
/// <see cref="FaultBudget"/> — et l'application continue.
/// </para>
/// </summary>
internal static class Guard
{
    private static readonly FaultBudget Faults = new();

    /// <summary>Un battement de minuteur gardé.</summary>
    public static global::Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object> Tick(
        global::Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        return (timer, args) =>
        {
            try
            {
                tick(timer, args);
            }
            catch (Exception ex)
            {
                Report("minuteur", ex);
            }
        };
    }

    /// <summary>Un battement de <c>DispatcherTimer</c> XAML gardé, comme les autres.</summary>
    public static EventHandler<object> XamlTick(EventHandler<object> tick)
    {
        ArgumentNullException.ThrowIfNull(tick);

        return (sender, args) =>
        {
            try
            {
                tick(sender, args);
            }
            catch (Exception ex)
            {
                Report("minuteur XAML", ex);
            }
        };
    }

    /// <summary>La fin d'un lot d'animations du compositeur, gardée : elle arrive par le même chemin qu'un minuteur.</summary>
    public static global::Windows.Foundation.TypedEventHandler<object, Microsoft.UI.Composition.CompositionBatchCompletedEventArgs> Batch(
        global::Windows.Foundation.TypedEventHandler<object, Microsoft.UI.Composition.CompositionBatchCompletedEventArgs> completed)
    {
        ArgumentNullException.ThrowIfNull(completed);

        return (sender, args) =>
        {
            try
            {
                completed(sender, args);
            }
            catch (Exception ex)
            {
                Report("fin d'animation", ex);
            }
        };
    }

    /// <summary>Un travail posté sur le fil d'interface, gardé.</summary>
    public static DispatcherQueueHandler Run(DispatcherQueueHandler work)
    {
        ArgumentNullException.ThrowIfNull(work);

        return () =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                Report("travail posté", ex);
            }
        };
    }

    /// <summary>
    /// Journalise une exception que l'application survit : les premières d'une
    /// minute en entier, les suivantes comptées.
    /// </summary>
    public static void Report(string where, Exception exception)
    {
        FaultVerdict verdict = Faults.Record(DateTimeOffset.UtcNow);

        if (verdict.Log)
        {
            string silenced = verdict.Silenced > 0 ? $" ({verdict.Silenced} autres tues depuis la précédente)" : string.Empty;
            MiniLogger.Log($"[ERREUR] Exception non gérée ({where}), la notch continue{silenced} : {exception}");
        }
    }
}
