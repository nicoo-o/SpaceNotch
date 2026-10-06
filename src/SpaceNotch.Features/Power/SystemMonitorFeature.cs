using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.SystemInfo;
using SpaceNotch.Platform.Windows.Power;

namespace SpaceNotch.Features.Power;

/// <summary>
/// Moniteur système (F6) : rien au repos. Si le processeur reste au-dessus
/// de 85 % pendant 20 secondes, une pastille rouge apparaît avec une
/// mini-courbe et le processus responsable ; ouverte, elle propose de le
/// fermer.
///
/// <para>
/// Exception documentée à la règle « aucune boucle » : Windows ne notifie pas
/// la charge du processeur. Une mesure toutes les deux secondes ne coûte qu'un
/// appel à <c>GetSystemTimes</c> ; la recherche du processus, plus chère, n'a
/// lieu qu'au déclenchement de l'alerte.
/// </para>
/// </summary>
public sealed class SystemMonitorFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Monitor;

    public const string ActivityId = "feature.monitor.cpu";

    public const string CloseAction = "monitor.close";

    /// <summary>Rouge de l'alerte.</summary>
    public static readonly ActivityTint Red = new(0xFF, 0x6B, 0x6B);

    /// <summary>Échantillonnage quand le processeur chauffe ou qu'une alerte est en cours.</summary>
    private static readonly TimeSpan BusyPeriod = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Échantillonnage au calme (audit SN-12) : l'alerte exige 20 s au-dessus du
    /// seuil, une mesure toutes les 5 s suffit à voir monter la charge, et le
    /// processeur d'un portable au repos est réveillé deux fois et demie moins.
    /// </summary>
    private static readonly TimeSpan CalmPeriod = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Fenêtre de confirmation : fermer une application peut faire perdre un
    /// travail non enregistré (un rendu, un document). Le premier clic arme
    /// le bouton, seul un second clic dans ce délai ferme.
    /// </summary>
    public static readonly TimeSpan ConfirmWindow = TimeSpan.FromSeconds(3);

    private readonly CpuSampler _sampler = new();
    private readonly CpuWatch _watch = new();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private HeavyProcess? _heaviest;
    private double _last;
    private volatile bool _sampling;
    private DateTimeOffset _armedUntil;

    public SystemMonitorFeature(IActivityManager activities, IEventBus events, bool isEnabled = true, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Moniteur", activities, events, isEnabled)
    {
        _now = now ?? (() => DateTimeOffset.UtcNow);
        _timer = new Timer(Guarded(Tick), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Vrai entre le premier et le second clic sur « Fermer ».</summary>
    public bool IsCloseArmed => _now() < _armedUntil;

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _sampler.Sample();
        _sampling = true;
        _timer.Change(CalmPeriod, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _sampling = false;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _timer.Dispose();

    private void Tick()
    {
        if (!_sampling)
        {
            return;
        }

        try
        {
            Add(_sampler.Sample(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            ReportError(ex);
        }

        if (!_sampling)
        {
            return;
        }

        bool busy;

        lock (_gate)
        {
            busy = _watch.IsAlerting || _last >= CpuWatch.Release;
        }

        try
        {
            _timer.Change(busy ? BusyPeriod : CalmPeriod, Timeout.InfiniteTimeSpan);
        }
        catch (ObjectDisposedException)
        {
            // Arrêtée pendant la mesure.
        }
    }

    /// <summary>Une mesure. Appelable directement (tests, visite).</summary>
    public void Add(double percent, DateTimeOffset now, HeavyProcess? heaviest = null)
    {
        CpuVerdict verdict;

        lock (_gate)
        {
            verdict = _watch.Add(percent, now);
            _last = percent;
        }

        switch (verdict)
        {
            case CpuVerdict.Alert:
                _heaviest = heaviest ?? CpuSampler.FindHeaviest(TimeSpan.FromMilliseconds(400));
                Publish();
                break;

            case CpuVerdict.Ongoing:
                Publish();
                break;

            case CpuVerdict.Clear:
                _heaviest = null;
                RemoveActivity(ActivityId);
                break;
        }
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.ActionId != CloseAction || _heaviest is not { } process)
        {
            return Task.FromResult(false);
        }

        // Premier clic : le bouton demande confirmation, rien n'est fermé.
        if (!IsCloseArmed)
        {
            _armedUntil = _now() + ConfirmWindow;
            Publish();
            return Task.FromResult(true);
        }

        _armedUntil = default;

        if (CpuSampler.TryClose(process.Id))
        {
            _heaviest = null;
            RemoveActivity(ActivityId);
        }
        else
        {
            Publish();
        }

        return Task.FromResult(true);
    }

    private void Publish()
    {
        HeavyProcess? process = _heaviest;
        string name = process?.Name ?? Lang.T("Processeur", "Processor");
        string load = _last.ToString("0", CultureInfo.CurrentCulture) + " %";

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Monitor,
            Title = name,
            Subtitle = Lang.T("Processeur ", "Processor ") + load,
            Source = Lang.T("Moniteur", "Monitor"),
            IconKey = "Cpu",
            Tint = Red,
            Metric = load,
            State = IslandActivityState.Idle,
            // Normale, pas haute : la pastille se montre sans ouvrir la notch
            // d'elle-même ; un clic l'ouvre.
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new MonitorPayload(_watch.History, name, process?.Id ?? 0, process?.Percent ?? 0),
            // Armé, le libellé change : c'est la confirmation. Il revient de
            // lui-même au relevé suivant une fois le délai passé.
            Actions = process is null
                ? []
                : [IsCloseArmed
                    ? new ActivityAction(CloseAction, Lang.T("Forcer la fermeture ?", "Force quit?"), "Close", ActivityActionKind.Invoke, IsPrimary: true)
                    : new ActivityAction(CloseAction, Lang.T($"Fermer {name}", $"Close {name}"), "Close", ActivityActionKind.Invoke, IsPrimary: true)]
        });
    }
}
