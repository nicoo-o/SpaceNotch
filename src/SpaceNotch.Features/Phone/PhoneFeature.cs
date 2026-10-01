using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Phone;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Phone;

/// <summary>
/// Le téléphone dans la notch (T1, T2), par Lien avec Windows : un appel qui
/// sonne s'ouvre en vert, « Répondre » ouvre Lien avec Windows, puis une
/// pastille compte la durée ; une livraison ou un VTC suit ses trois étapes
/// avec son véhicule en pixels et l'heure d'arrivée.
///
/// <para>
/// Rien n'est demandé au téléphone : la fonctionnalité lit les notifications
/// que Windows reçoit déjà. Celles qu'elle comprend ne s'affichent pas en
/// double dans les notifications.
/// </para>
/// </summary>
public sealed class PhoneFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Phone;

    public const string CallActivityId = "feature.phone.call";

    public const string DeliveryActivityId = "feature.phone.delivery";

    public const string AnswerAction = "phone.answer";

    public const string DeclineAction = "phone.decline";

    public const string HangUpAction = "phone.hangup";

    public const string CallBackAction = "phone.callback";

    public const string DeliveryDoneAction = "phone.delivery.done";

    /// <summary>Le vert d'un appel.</summary>
    public static readonly ActivityTint CallGreen = new(0x5B, 0xE3, 0x8A);

    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _now;
    private readonly Timer _clock;
    private string? _caller;
    private DateTimeOffset? _callStart;
    private DeliveryUpdate? _delivery;
    private DateTimeOffset _deliverySince;

    public PhoneFeature(IActivityManager activities, IEventBus events, bool isEnabled = true, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Téléphone", activities, events, isEnabled)
    {
        _now = now ?? (() => DateTimeOffset.Now);
        _clock = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    /// <summary>Ouvrir Lien avec Windows (l'application fournit le lancement d'adresse).</summary>
    public event Action<string>? OpenRequested;

    /// <summary>Un appel est en cours (sonne ou décroché).</summary>
    public bool InCall
    {
        get
        {
            lock (_gate)
            {
                return _caller is not null;
            }
        }
    }

    /// <summary>
    /// Une notification : rend <c>true</c> si elle a été comprise (appel ou
    /// livraison) et ne doit pas s'afficher une seconde fois.
    /// </summary>
    public bool Offer(string appName, string title, string body)
    {
        if (!IsEnabled)
        {
            return false;
        }

        if (PhoneLink.ReadCall(appName, title, body) is { } call)
        {
            ShowCall(call);
            return true;
        }

        if (Delivery.Read(appName, title, body, _now()) is { } delivery)
        {
            ShowDelivery(delivery);
            return true;
        }

        return false;
    }

    public void ShowCall(PhoneCall call)
    {
        ArgumentNullException.ThrowIfNull(call);

        switch (call.State)
        {
            case CallState.Ringing:
                lock (_gate)
                {
                    _caller = call.Caller;
                    _callStart = null;
                }

                PublishActivity(Ringing(call.Caller));
                break;

            case CallState.Active:
                lock (_gate)
                {
                    _caller = call.Caller;
                    _callStart ??= _now();
                }

                PublishActivity(Active());
                Arm();
                break;

            case CallState.Missed:
                lock (_gate)
                {
                    _caller = null;
                    _callStart = null;
                }

                PublishActivity(Missed(call.Caller));
                break;

            default:
                EndCall();
                break;
        }
    }

    public void ShowDelivery(DeliveryUpdate update)
    {
        ArgumentNullException.ThrowIfNull(update);

        lock (_gate)
        {
            // Un autre service (ou une nouvelle commande) : la frise repart.
            if (_delivery is null || _delivery.Service != update.Service || update.Step < _delivery.Step)
            {
                _deliverySince = _now();
            }
            else if (update.Step > _delivery.Step)
            {
                _deliverySince = _now();
            }

            // Une étape sans heure garde l'heure déjà connue.
            _delivery = update.Eta is null && update.Step != DeliveryStep.Arrived && _delivery?.Service == update.Service
                ? update with { Eta = _delivery.Eta }
                : update;
        }

        PublishDelivery();
        Arm();
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActivityId, request.ActionId)
        {
            case (CallActivityId, AnswerAction):
                OpenRequested?.Invoke(PhoneLink.CallingUri);

                lock (_gate)
                {
                    _callStart ??= _now();
                }

                PublishActivity(Active());
                Arm();
                return Task.FromResult(true);

            case (CallActivityId, CallBackAction):
                OpenRequested?.Invoke(PhoneLink.CallingUri);
                RemoveActivity(CallActivityId);
                return Task.FromResult(true);

            case (CallActivityId, DeclineAction):
            case (CallActivityId, HangUpAction):
                // Le téléphone garde la main : on ouvre Lien avec Windows pour raccrocher là-bas.
                if (request.ActionId == HangUpAction)
                {
                    OpenRequested?.Invoke(PhoneLink.CallingUri);
                }

                EndCall();
                return Task.FromResult(true);

            case (DeliveryActivityId, DeliveryDoneAction):
                lock (_gate)
                {
                    _delivery = null;
                }

                RemoveActivity(DeliveryActivityId);
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    protected override Task OnStartAsync(System.Threading.CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        _clock.Change(Timeout.Infinite, Timeout.Infinite);
        RemoveActivity(CallActivityId);
        RemoveActivity(DeliveryActivityId);

        lock (_gate)
        {
            _caller = null;
            _callStart = null;
            _delivery = null;
        }

        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _clock.Dispose();

    /// <summary>Met à jour la durée de l'appel et la position du véhicule. Appelable directement (tests, visite).</summary>
    public void Tick()
    {
        bool call, delivery;

        lock (_gate)
        {
            call = _callStart is not null;
            delivery = _delivery is { Step: not DeliveryStep.Arrived };
        }

        if (call)
        {
            PublishActivity(Active());
        }

        if (delivery)
        {
            PublishDelivery();
        }

        if (!call && !delivery)
        {
            _clock.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void Arm()
    {
        bool call;

        lock (_gate)
        {
            call = _callStart is not null;
        }

        // La seconde pour un appel, la demi-minute pour une livraison.
        TimeSpan every = call ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30);
        _clock.Change(every, every);
    }

    private void EndCall()
    {
        lock (_gate)
        {
            _caller = null;
            _callStart = null;
        }

        RemoveActivity(CallActivityId);
        Arm();
    }

    private static IslandActivity Ringing(string caller) => new()
    {
        Id = CallActivityId,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = caller,
        Subtitle = Lang.T("Appel entrant · téléphone", "Incoming call · phone"),
        Source = Lang.T("Téléphone", "Phone"),
        IconKey = "Call",
        Tint = CallGreen,
        State = IslandActivityState.Idle,
        MotionState = ActivityMotionState.Attention,
        Priority = ActivityPriority.Critical,
        Duration = TimeSpan.FromSeconds(45),
        Actions =
        [
            new ActivityAction(AnswerAction, Lang.T("Répondre", "Answer"), "Call", ActivityActionKind.Invoke, IsPrimary: true),
            new ActivityAction(DeclineAction, Lang.T("Ignorer", "Dismiss"), "Close")
        ]
    };

    private IslandActivity Active()
    {
        string caller;
        TimeSpan elapsed;

        lock (_gate)
        {
            caller = _caller ?? Lang.T("Appel", "Call");
            elapsed = _now() - (_callStart ?? _now());
        }

        return new IslandActivity
        {
            Id = CallActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Title = caller,
            Subtitle = Lang.T("Appel en cours · Lien avec Windows", "On a call · Phone Link"),
            Source = Lang.T("Téléphone", "Phone"),
            IconKey = "Call",
            Metric = PhoneLink.Duration(elapsed),
            Tint = CallGreen,
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            Policy = ActivityPresentationPolicy.Passive,
            Actions = [new ActivityAction(HangUpAction, Lang.T("Raccrocher", "Hang up"), "Close", ActivityActionKind.Invoke, IsPrimary: true)]
        };
    }

    private static IslandActivity Missed(string caller) => new()
    {
        Id = CallActivityId,
        FeatureId = FeatureKey,
        SceneKey = IslandSceneCatalog.Card,
        Title = caller,
        Subtitle = Lang.T("Appel manqué", "Missed call"),
        Source = Lang.T("Téléphone", "Phone"),
        IconKey = "Call",
        Tint = new ActivityTint(0xFF, 0x7A, 0x6B),
        State = IslandActivityState.Idle,
        Priority = ActivityPriority.Normal,
        Duration = TimeSpan.FromMinutes(2),
        Actions = [new ActivityAction(CallBackAction, Lang.T("Rappeler", "Call back"), "Call", ActivityActionKind.Invoke, IsPrimary: true)]
    };

    private void PublishDelivery()
    {
        DeliveryUpdate? delivery;
        DateTimeOffset since;

        lock (_gate)
        {
            delivery = _delivery;
            since = _deliverySince;
        }

        if (delivery is null)
        {
            return;
        }

        DateTimeOffset now = _now();
        bool arrived = delivery.Step == DeliveryStep.Arrived;
        string? eta = delivery.Eta is { } at
            ? Lang.T("Arrivée vers ", "Arriving around ") + at.ToLocalTime().ToString("t", CultureInfo.CurrentCulture)
            : null;

        PublishActivity(new IslandActivity
        {
            Id = DeliveryActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Card,
            Eyebrow = delivery.Service,
            Title = Delivery.StepLabel(delivery.Step, delivery.Kind, Lang.French),
            Subtitle = eta ?? (arrived ? Lang.T("Va à la porte", "Head to the door") : null),
            Source = delivery.Service,
            IconKey = Delivery.VehicleGlyph(delivery.Kind),
            Metric = !arrived && delivery.Eta is { } e ? Countdown(e - now) : null,
            Tint = TintFor(delivery.Service),
            State = IslandActivityState.Idle,
            MotionState = arrived ? ActivityMotionState.Attention : ActivityMotionState.Idle,
            Priority = arrived ? ActivityPriority.High : ActivityPriority.Normal,
            Policy = arrived ? null : ActivityPresentationPolicy.Passive,
            Duration = arrived ? TimeSpan.FromMinutes(5) : TimeSpan.FromHours(2),
            Payload = new DeliveryPayload(delivery.Service, delivery.Kind, delivery.Step, delivery.Eta, since),
            Actions = arrived ? [new ActivityAction(DeliveryDoneAction, "OK", "Check", ActivityActionKind.Invoke, IsPrimary: true)] : []
        });
    }

    /// <summary>« 12 min », « 1 min », « maintenant ».</summary>
    public static string Countdown(TimeSpan left)
        => left <= TimeSpan.FromSeconds(30)
            ? Lang.T("maintenant", "now")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min");

    private static ActivityTint TintFor(string service) => service switch
    {
        "Uber Eats" => new ActivityTint(0x06, 0xC1, 0x67),
        "Deliveroo" => new ActivityTint(0x00, 0xCC, 0xBC),
        "Bolt" => new ActivityTint(0x34, 0xD1, 0x86),
        "Uber" => new ActivityTint(0xE8, 0xE8, 0xE8),
        "Just Eat" => new ActivityTint(0xFF, 0x80, 0x00),
        "Heetch" => new ActivityTint(0xFF, 0x4E, 0x8A),
        _ => new ActivityTint(0xFF, 0xC8, 0x6B)
    };
}
