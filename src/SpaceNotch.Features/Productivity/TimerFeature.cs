using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Localization;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Productivity;
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

    /// <summary>Activité du minuteur : la tuile du tableau de bord la présente quand il tourne.</summary>
    public const string ActivityId = "feature.timer.current";

    private static readonly TimeSpan DefaultCountdown = TimeSpan.FromMinutes(5);

    private readonly Timer _tick;

    private readonly MeasureClock _clock;

    private TimeSpan? _published;


    public TimerFeature(IActivityManager activities, IEventBus events, bool isEnabled = true, Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Minuteur", activities, events, isEnabled)
    {
        _clock = new MeasureClock(now);
        _clock.Set(DefaultCountdown, countsDown: true);
        _tick = new Timer(Guarded(() => OnTick(null)), null, Timeout.Infinite, Timeout.Infinite);
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
    public bool IsMeasuring => _clock.IsRunning;

    /// <summary>Démarre une mesure, ou la suspend si elle est déjà en cours.</summary>
    public void Toggle()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (_clock.IsRunning)
        {
            _clock.Pause();
            StopTick();
        }
        else
        {
            _clock.Start();
            StartTick();
        }

        Publish();
    }

    /// <summary>Lance un compte à rebours de la durée donnée : les puces 5 / 15 / 25 min du menu rapide.</summary>
    public void StartCountdown(TimeSpan duration)
    {
        if (!IsEnabled || duration <= TimeSpan.Zero)
        {
            return;
        }

        Mode = TimerMode.Countdown;
        _clock.Set(duration, countsDown: true);
        _clock.Start();
        StartTick();

        Publish();
    }

    /// <summary>Remet la mesure à zéro et l'arrête.</summary>
    public void Reset()
    {
        StopTick();
        _clock.Set(Mode == TimerMode.Countdown ? DefaultCountdown : TimeSpan.Zero, Mode == TimerMode.Countdown);

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
        StopTick();
        _clock.Set(Mode == TimerMode.Countdown ? DefaultCountdown : TimeSpan.Zero, Mode == TimerMode.Countdown);

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
        if (!_clock.IsRunning)
        {
            return;
        }

        // Le battement ne compte rien : il redessine. La valeur vient de
        // l'échéance, juste même après une mise en veille.
        if (!_clock.IsFinished)
        {
            // Même identifiant : l'activité est remplacée, jamais empilée —
            // et seulement quand la seconde affichée change.
            if (_clock.Value != _published)
            {
                Publish();
            }

            return;
        }

        StopTick();
        _clock.Set(TimeSpan.Zero, countsDown: true);

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Timer,
            Title = Lang.T("Temps écoulé", "Time’s up"),
            Subtitle = Lang.T("Minuteur", "Timer"),
            Source = "Timer",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            MotionState = ActivityMotionState.Completing,
            Duration = TimeSpan.FromSeconds(5),
            Payload = new TimerPayload(TimeSpan.Zero, false, Lang.T("Temps écoulé", "Time’s up"))
        });

        PublishEvent(new NotificationPostedEvent(Lang.T("Minuteur", "Timer"), Lang.T("Temps écoulé", "Time’s up"), Lang.T("La mesure est terminée.", "The timer has finished.")));
    }

    private void Publish()
    {
        _published = _clock.Value;

        string mode = Mode switch
        {
            TimerMode.Stopwatch => Lang.T("Chronomètre", "Stopwatch"),
            _ => Lang.T("Minuteur", "Timer")
        };

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Timer,
            Title = Format(_clock.Value),
            Subtitle = mode,
            Source = "Timer",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.Normal,
            Payload = new TimerPayload(_clock.Value, _clock.IsRunning, mode)
        });
    }

    private static string Format(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"h\:mm\:ss", System.Globalization.CultureInfo.InvariantCulture)
            : value.ToString(@"mm\:ss", System.Globalization.CultureInfo.InvariantCulture);

    private void StopTick() => _tick.Change(Timeout.Infinite, Timeout.Infinite);

    /// <summary>Un battement toutes les 250 ms : l'affichage change de seconde à l'heure juste, sans saut.</summary>
    private void StartTick() => _tick.Change(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
}
