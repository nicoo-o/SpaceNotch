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

    private static readonly TimeSpan Period = TimeSpan.FromSeconds(2);

    private readonly CpuSampler _sampler = new();
    private readonly CpuWatch _watch = new();
    private readonly Timer _timer;
    private readonly object _gate = new();
    private HeavyProcess? _heaviest;
    private double _last;

    public SystemMonitorFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, "Moniteur", activities, events, isEnabled)
    {
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _sampler.Sample();
        _timer.Change(Period, Period);
        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _timer.Dispose();

    private void Tick()
    {
        try
        {
            Add(_sampler.Sample(), DateTimeOffset.UtcNow);
        }
        catch (Exception ex)
        {
            ReportError(ex);
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

        if (CpuSampler.TryClose(process.Id))
        {
            _heaviest = null;
            RemoveActivity(ActivityId);
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
            Priority = ActivityPriority.High,
            Policy = ActivityPresentationPolicy.Passive,
            Payload = new MonitorPayload(_watch.History, name, process?.Id ?? 0, process?.Percent ?? 0),
            Actions = process is null
                ? []
                : [new ActivityAction(CloseAction, Lang.T($"Fermer {name}", $"Close {name}"), "Close", ActivityActionKind.Invoke, IsPrimary: true)]
        });
    }
}
