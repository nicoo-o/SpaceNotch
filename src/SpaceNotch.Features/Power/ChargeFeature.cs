using System;
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
/// Charge de la batterie (F1) : brancher le portable élargit la notch un
/// instant — une batterie en pixels se remplit jusqu'au niveau réel, le
/// pourcentage s'inscrit, l'éclair vert s'allume — puis la notch revient au
/// repos. Événements de <c>PowerManager</c> seulement, aucune scrutation.
/// </summary>
public sealed class ChargeFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Charge;

    public const string ActivityId = "feature.charge.plugged";

    /// <summary>Vert de la charge, le même que la coche.</summary>
    public static readonly ActivityTint Green = new(0x5F, 0xE0, 0x8A);

    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(4);

    private readonly PowerWatcher? _watcher;
    private readonly ChargeWatch _watch = new();
    private readonly object _gate = new();

    public ChargeFeature(IActivityManager activities, IEventBus events, PowerWatcher? watcher, bool isEnabled = true)
        : base(FeatureKey, "Charge", activities, events, isEnabled)
    {
        _watcher = watcher;
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        if (_watcher is not null)
        {
            _watcher.Changed += Report;
            _watcher.Start();
        }

        return Task.CompletedTask;
    }

    protected override Task OnStopAsync()
    {
        if (_watcher is not null)
        {
            _watcher.Changed -= Report;
            _watcher.Stop();
        }

        return Task.CompletedTask;
    }

    /// <summary>Nouvel état de l'alimentation. Appelable directement (tests, visite).</summary>
    public void Report(bool hasBattery, bool plugged, int percent)
    {
        bool announce;

        lock (_gate)
        {
            announce = _watch.Update(hasBattery, plugged, percent);
        }

        if (announce)
        {
            Announce(_watch.Percent);
        }
    }

    /// <summary>Montre le branchement au niveau donné.</summary>
    public void Announce(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = Lang.T("En charge", "Charging"),
            Subtitle = Lang.T("Chargeur branché", "Charger connected"),
            Source = Lang.T("Batterie", "Battery"),
            IconKey = "Bolt",
            Tint = Green,
            Progress = percent / 100.0,
            Metric = percent.ToString(System.Globalization.CultureInfo.CurrentCulture) + " %",
            State = IslandActivityState.DeviceActive,
            Priority = ActivityPriority.Normal,
            Policy = ActivityPresentationPolicy.Temporary,
            Duration = Lifetime,
            Payload = new ChargePayload(percent)
        });
    }
}
