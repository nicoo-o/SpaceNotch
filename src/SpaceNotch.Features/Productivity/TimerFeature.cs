using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;

namespace SpaceNotch.Features.Productivity;

/// <summary>
/// Sens de comptage du minuteur.
/// </summary>
public enum TimerMode
{
    /// <summary>Compte à rebours depuis une durée fixée.</summary>
    Countdown,

    /// <summary>Compte le temps écoulé, sans échéance.</summary>
    Stopwatch
}

/// <summary>
/// Minuteur et chronomètre.
///
/// Même exception documentée que le minuteur de focus : un compteur doit
/// avancer, donc il bat à une seconde. Les garde-fous sont les mêmes — le
/// minuteur est créé suspendu, il ne bat que pendant une mesure active, et il est
/// arrêté par la mise en pause, la remise à zéro, la désactivation de la
/// fonctionnalité et la fermeture de l'application. Hors mesure, la
/// fonctionnalité ne consomme rien.
/// </summary>
public sealed class TimerFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.timer";

    public const string ToggleAction = "timer.toggle";

    public const string ResetAction = "timer.reset";

    private const string ActivityId = "feature.timer.current";

    private static readonly TimeSpan DefaultCountdown = TimeSpan.FromMinutes(5);

    private readonly Timer _tick;

    private TimeSpan _value = DefaultCountdown;
    private bool _running;

    public TimerFeature(IActivityManager activities, IEventBus events, bool isEnabled = true)
        : base(FeatureKey, "Minuteur", activities, events, isEnabled)
    {
        _tick = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public TimerMode Mode { get; private set; } = TimerMode.Countdown;

    /// <summary>
    /// Vrai lorsqu'une mesure avance.
    ///
    /// Le nom est explicite parce que <see cref="IslandFeatureBase.IsRunning"/>
    /// désigne autre chose : l'état du cycle de vie de la fonctionnalité. Les
    /// confondre reviendrait à croire qu'un minuteur arrêté est une fonctionnalité
    /// éteinte.
    /// </summary>
    public bool IsMeasuring => _running;

    /// <summary>Démarre une mesure, ou la suspend si elle est déjà en cours.</summary>
    public void Toggle()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (_running)
        {
            _running = false;
            StopTick();
        }
        else
        {
            _running = true;
            _tick.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        }

        Publish();
    }

    /// <summary>Remet la mesure à zéro et l'arrête.</summary>
    public void Reset()
    {
        _running = false;
        StopTick();

        _value = Mode == TimerMode.Countdown ? DefaultCountdown : TimeSpan.Zero;

        Publish();
    }

    /// <summary>
    /// Change de mode et remet à zéro. Le mode est porté par la fonctionnalité,
    /// jamais par la vue : celle-ci ne fait qu'afficher ce qu'on lui déclare.
    /// </summary>
    public void SetMode(TimerMode mode)
    {
        Mode = mode;
        Reset();
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        // Désactiver la fonctionnalité arrête la mesure : un compteur qui
        // continuerait de battre pour une fonctionnalité éteinte serait exactement
        // le travail inutile que le projet s'interdit.
        _running = false;
        StopTick();

        _value = Mode == TimerMode.Countdown ? DefaultCountdown : TimeSpan.Zero;

        return Task.CompletedTask;
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case ToggleAction:
                Toggle();
                return Task.FromResult(true);

            case ResetAction:
                Reset();
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    protected override void OnDisposed() => _tick.Dispose();

    private void OnTick(object? state)
    {
        if (!_running)
        {
            return;
        }

        if (Mode == TimerMode.Stopwatch)
        {
            _value += TimeSpan.FromSeconds(1);
            Publish();
            return;
        }

        if (_value > TimeSpan.Zero)
        {
            _value -= TimeSpan.FromSeconds(1);

            // Même identifiant : l'activité est remplacée, jamais empilée.
            Publish();
            return;
        }

        _running = false;
        StopTick();

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Timer,
            Title = "Temps écoulé",
            Subtitle = "Minuteur",
            Source = "Timer",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            Duration = TimeSpan.FromSeconds(5),
            Payload = new TimerPayload(TimeSpan.Zero, false, "Temps écoulé")
        });

        PublishEvent(new NotificationPostedEvent("Minuteur", "Temps écoulé", "La mesure est terminée."));
    }

    private void Publish()
    {
        string mode = Mode switch
        {
            TimerMode.Stopwatch => "Chronomètre",
            _ => "Minuteur"
        };

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Timer,
            Title = Format(_value),
            Subtitle = mode,
            Source = "Timer",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.Normal,
            Payload = new TimerPayload(_value, _running, mode)
        });
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

    private void StopTick() => _tick.Change(Timeout.Infinite, Timeout.Infinite);
}
