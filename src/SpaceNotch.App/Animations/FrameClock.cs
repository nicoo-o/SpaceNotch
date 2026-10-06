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
    private static bool _runStarted;

    /// <summary>Vrai pendant que les abonnés sont appelés pour une image.</summary>
    private static bool _raising;

    /// <summary>Le dernier abonné est parti pendant l'image : la rafale se clôt à sa fin.</summary>
    private static bool _closePending;

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
            queue.TryEnqueueSafely(() => action());
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

        if (!_runStarted)
        {
            _runStarted = true;
            _runStart = SafeContext();
        }

        FrameCosts.Clear();
        long started = Stopwatch.GetTimestamp();
        _raising = true;

        try
        {
            Fan.Raise(sender, e);
        }
        finally
        {
            _raising = false;
        }

        double cost = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

        // Une image qui dépasse le budget de 60 Hz se voit : on dit qui l'a prise.
        if (cost > FrameRunStats.Budget60)
        {
            LogSlowFrame(cost);
        }

        // Sans heure de rendu, l'intervalle est inconnu (NaN), jamais compté
        // depuis zéro : il passerait pour une pause aussi longue que la session.
        double interval = frame is { } now && _lastFrame is { } last ? (now - last).TotalMilliseconds : double.NaN;
        _lastFrame = frame ?? _lastFrame;
        run.Add(interval, cost);

        // Le dernier abonné est parti pendant cette image : la rafale se clôt
        // maintenant, avec elle, et pas au milieu (voir Detach).
        if (_closePending)
        {
            _closePending = false;
            CloseRun();
        }
    }

    private static void Detach()
    {
        CompositionTarget.Rendering -= OnRendering;

        // Un abonné qui se retire dans son propre gestionnaire (fin d'un
        // passage) peut être le dernier : clore ici, au milieu de l'image,
        // versait cette image dans la rafale suivante avec un début périmé.
        if (_raising)
        {
            _closePending = true;
            return;
        }

        CloseRun();
    }

    private static void CloseRun()
    {
        if (_run is { } run && run.Finish() is { } report)
        {
            MiniLogger.Log(report.ToLogLine(_runStart + " → " + SafeContext()));
        }

        _lastFrame = null;
        _runStarted = false;
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
        // Un rendu lancé depuis un abonné (fin de ressort) est déjà compté dans
        // l'image lente de cet abonné : le journaliser aussi le compterait deux fois.
        if (_run is not null && !_raising && ms > FrameRunStats.Budget60)
        {
            MiniLogger.Log(string.Format(FrameRunStats.LogCulture, "[IMAGES] {0} lent {1:0.0} ms ({2})", what, ms, SafeContext()));
        }
    }

    /// <summary>
    /// Détail d'une étape lourde (la géométrie de la notch) : journalisé seulement
    /// au-delà du budget de 120 Hz, pour désigner la sous-étape qui coûte.
    /// </summary>
    /// <param name="what">Nom de l'étape.</param>
    /// <param name="started">Horodatage <see cref="Stopwatch.GetTimestamp"/> du début.</param>
    /// <param name="marks">Fin de chaque sous-étape, dans l'ordre.</param>
    public static void ReportBreakdown(string what, long started, params (string Name, long At)[] marks)
    {
        if (_run is null || marks.Length == 0)
        {
            return;
        }

        double total = Stopwatch.GetElapsedTime(started, marks[^1].At).TotalMilliseconds;

        if (total <= FrameRunStats.Budget120)
        {
            return;
        }

        var parts = new System.Text.StringBuilder();
        long previous = started;

        foreach ((string name, long at) in marks)
        {
            parts.Append(parts.Length == 0 ? string.Empty : ", ")
                .Append(name).Append(' ')
                .Append(Stopwatch.GetElapsedTime(previous, at).TotalMilliseconds.ToString("0.0", FrameRunStats.LogCulture));
            previous = at;
        }

        MiniLogger.Log(string.Format(FrameRunStats.LogCulture, "[IMAGES] {0} lente {1:0.0} ms : {2}", what, total, parts));
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
                .Append(' ').Append(ms.ToString("0.0", FrameRunStats.LogCulture)).Append(" ms");
        }

        MiniLogger.Log(string.Format(FrameRunStats.LogCulture, "[IMAGES] image lente {0:0.0} ms ({1}) : {2}", cost, SafeContext(), parts));
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
