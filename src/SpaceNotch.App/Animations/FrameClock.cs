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
        Fan.Timed = (handler, elapsed) => FrameCosts.Add((handler, elapsed.TotalMilliseconds));
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

        FrameCosts.Clear();
        long started = Stopwatch.GetTimestamp();
        Fan.Raise(sender, e);
        double cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Une image qui dépasse le budget de 60 Hz se voit : on dit qui l'a prise.
        if (cost > FrameRunStats.Budget60)
        {
            LogSlowFrame(cost);
        }
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

    /// <summary>Vrai quand la mesure de fluidité est allumée (<c>--frames</c>).</summary>
    public static bool Measuring => _run is not null;

    /// <summary>
    /// Un travail du fil d'interface hors de l'horloge (le rendu de la fenêtre) :
    /// journalisé s'il dépasse le budget de 60 Hz, car il gèle les animations
    /// sans apparaître dans le coût des abonnés.
    /// </summary>
    public static void ReportSlow(string what, double ms)
    {
        if (_run is not null && ms > FrameRunStats.Budget60)
        {
            MiniLogger.Log(string.Format(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), "[IMAGES] {0} lent {1:0.0} ms ({2})", what, ms, SafeContext()));
        }
    }

    /// <summary>Coût de chaque abonné dans l'image en cours (mesure seulement).</summary>
    private static readonly System.Collections.Generic.List<(EventHandler<object> Handler, double Ms)> FrameCosts = [];

    private static void LogSlowFrame(double cost)
    {
        FrameCosts.Sort((a, b) => b.Ms.CompareTo(a.Ms));
        var parts = new System.Text.StringBuilder();

        for (int i = 0; i < Math.Min(3, FrameCosts.Count); i++)
        {
            (EventHandler<object> handler, double ms) = FrameCosts[i];
            parts.Append(i == 0 ? string.Empty : ", ")
                .Append(handler.Method.DeclaringType?.Name).Append('.').Append(handler.Method.Name)
                .Append(' ').Append(ms.ToString("0.0", System.Globalization.CultureInfo.GetCultureInfo("fr-FR"))).Append(" ms");
        }

        MiniLogger.Log(string.Format(System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), "[IMAGES] image lente {0:0.0} ms ({1}) : {2}", cost, SafeContext(), parts));
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
