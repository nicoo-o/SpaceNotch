using System;
using System.Diagnostics;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Animation;
using SpaceNotch.Infrastructure.Logging;

namespace SpaceNotch_App.Animations;

/// <summary>
/// L'horloge d'images de l'application (phase D) : remplace les abonnements
/// directs à <see cref="CompositionTarget.Rendering"/>. Même forme
/// d'événement, donc même usage ; un seul abonnement réel, posé seulement
/// quand quelque chose bouge.
///
/// <para>
/// Fil d'interface uniquement : un abonnement venu d'un autre fil y est
/// renvoyé (v1.16.1). Fait sur place, il échouait et figeait toutes les
/// animations — la notch restait vide, sans yeux, jusqu'au redémarrage.
/// </para>
/// </summary>
public static class FrameClock
{
    private static readonly FrameFanOut Fan = new(
        () => CompositionTarget.Rendering += OnRendering,
        Detach,
        ex => MiniLogger.Log("[ANIMATION] une animation a échoué ; elle est arrêtée, les autres continuent", ex));

    private static DispatcherQueue? _queue;

    /// <summary>Mesure de fluidité (<c>--frames</c>) : null quand elle est éteinte, ce qui est le cas par défaut.</summary>
    private static FrameRunStats? _run;

    private static Func<string>? _context;
    private static string _runStart = string.Empty;
    private static TimeSpan? _lastFrame;

    /// <summary>Retient le fil d'interface. Appelé une fois, depuis ce fil.</summary>
    public static void Attach(DispatcherQueue queue) => _queue ??= queue;

    /// <summary>
    /// Allume la mesure de fluidité : une ligne <c>[IMAGES]</c> par rafale,
    /// avec l'état de la notch au début et à la fin de la rafale.
    /// </summary>
    public static void MeasureRuns(Func<string> context)
    {
        _context = context;
        _run ??= new FrameRunStats();
        MiniLogger.Log("[IMAGES] mesure de fluidité allumée");
    }

    /// <summary>Une image va être dessinée.</summary>
    public static event EventHandler<object> Rendering
    {
        add => OnUiThread(() => Fan.Add(value));
        remove => OnUiThread(() => Fan.Remove(value));
    }

    /// <summary>Nombre d'animations abonnées (diagnostics).</summary>
    public static int Subscribers => Fan.Count;

    private static void OnUiThread(Action action)
    {
        if (_queue is { HasThreadAccess: false } queue)
        {
            queue.TryEnqueue(() => action());
            return;
        }

        action();
    }

    private static void OnRendering(object? sender, object e)
    {
        if (_run is not { } run)
        {
            Fan.Raise(sender, e);
            return;
        }

        // L'intervalle se lit sur l'heure de rendu de l'image, pas sur l'heure
        // de l'appel : c'est elle qui dit si une image a été sautée.
        TimeSpan? frame = (e as RenderingEventArgs)?.RenderingTime;

        if (_lastFrame is null)
        {
            _runStart = SafeContext();
        }

        long started = Stopwatch.GetTimestamp();
        Fan.Raise(sender, e);
        double cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        double interval = frame is { } now && _lastFrame is { } last ? (now - last).TotalMilliseconds : double.NaN;
        _lastFrame = frame ?? _lastFrame ?? TimeSpan.Zero;
        run.Add(interval, cost);
    }

    private static void Detach()
    {
        CompositionTarget.Rendering -= OnRendering;

        if (_run is { } run && run.Finish() is { } report)
        {
            MiniLogger.Log(report.ToLogLine(_runStart + " → " + SafeContext()));
        }

        _lastFrame = null;
    }

    private static string SafeContext()
    {
        try
        {
            return _context?.Invoke() ?? "?";
        }
        catch (Exception ex)
        {
            return "? (" + ex.GetType().Name + ")";
        }
    }
}
