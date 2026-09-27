using System;
using System.Globalization;
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
/// Session de travail minutée.
///
/// Exception documentée à la règle « aucune boucle » : un compte à rebours doit
/// avancer, donc il bat (quatre fois par seconde, pour changer de seconde à
/// l'heure juste ; la valeur, elle, vient d'une échéance et ne dérive pas). Quatre garde-fous le maintiennent dans un budget
/// borné et honnête : le minuteur n'existe que pendant une session active (il est
/// suspendu en pause, à la remise à zéro, à la désactivation de la fonctionnalité
/// et à la fermeture de l'application) ; l'activité est mise à jour sous un
/// identifiant stable, donc remplacée et jamais empilée ; le stockage du minuteur
/// ne coûte rien à l'arrêt, puisqu'il est créé suspendu ; et aucune donnée n'est
/// conservée quand la session s'achève.
/// </summary>
public sealed class PomodoroFeature : IslandFeatureBase
{
    public const string FeatureKey = FeatureKeys.Pomodoro;

    private const string ActivityId = "feature.pomodoro.session";

    private static readonly TimeSpan DefaultSessionLength = TimeSpan.FromMinutes(25);

    private readonly Timer _tickTimer;

    private readonly MeasureClock _clock;
    private TimeSpan? _published;

    public PomodoroFeature(
        IActivityManager activities,
        IEventBus events,
        bool isEnabled = true,
        Func<DateTimeOffset>? now = null)
        : base(FeatureKey, "Minuteur de focus", activities, events, isEnabled)
    {
        // La valeur vient d'une échéance : juste après une mise en veille, sans
        // dérive d'un battement en retard.
        _clock = new MeasureClock(now);
        _clock.Set(DefaultSessionLength, countsDown: true);

        // Créé suspendu : une fonctionnalité au repos ne consomme rien.
        _tickTimer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsSessionRunning => _clock.IsRunning;

    public TimeSpan Remaining => _clock.Value;

    /// <summary>Durée de la session en cours : l'anneau autour de la notch (F11) se lit contre elle.</summary>
    public TimeSpan SessionLength { get; private set; } = DefaultSessionLength;

    /// <summary>Part du temps qui reste, de 1 au départ à 0 à la fin.</summary>
    public double RemainingFraction
        => SessionLength <= TimeSpan.Zero ? 0 : Math.Clamp(Remaining / SessionLength, 0, 1);

    public void Start(TimeSpan? duration = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (duration.HasValue)
        {
            bool wasRunning = _clock.IsRunning;
            _clock.Set(duration.Value, countsDown: true);
            SessionLength = duration.Value;

            if (wasRunning)
            {
                _clock.Start();
            }
        }

        if (_clock.IsRunning)
        {
            return;
        }

        _clock.Start();
        _tickTimer.Change(TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        PublishSessionActivity(Lang.T("Focus en cours", "Focusing"));
    }

    public void Pause()
    {
        if (!_clock.IsRunning)
        {
            return;
        }

        _clock.Pause();
        StopTimer();
        PublishSessionActivity(Lang.T("En pause", "On a break"));
    }

    public void Reset(TimeSpan? duration = null)
    {
        StopTimer();
        _clock.Set(duration ?? DefaultSessionLength, countsDown: true);
        SessionLength = duration ?? DefaultSessionLength;
        RemoveActivity(ActivityId);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        // Désactiver la fonctionnalité interrompt la session : le minuteur ne doit
        // pas continuer à battre pour une fonctionnalité éteinte.
        StopTimer();
        _clock.Set(DefaultSessionLength, countsDown: true);

        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _tickTimer.Dispose();

    private void OnTick(object? state)
    {
        if (!_clock.IsRunning)
        {
            return;
        }

        if (!_clock.IsFinished)
        {
            // Même identifiant : l'activité est remplacée, pas empilée — et
            // seulement quand la seconde affichée change.
            if (_clock.Value != _published)
            {
                PublishSessionActivity(Lang.T("Focus en cours", "Focusing"));
            }

            return;
        }

        StopTimer();
        _clock.Set(DefaultSessionLength, countsDown: true);
        SessionLength = DefaultSessionLength;

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Pomodoro,
            Title = Lang.T("Session terminée", "Session complete"),
            Subtitle = Lang.T("Prenez une pause", "Take a break"),
            Source = "Pomodoro",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            MotionState = ActivityMotionState.Completing,
            Duration = TimeSpan.FromSeconds(5)
        });

        PublishEvent(new NotificationPostedEvent("Pomodoro", Lang.T("Session terminée", "Session complete"), Lang.T("Prenez une pause", "Take a break")));
    }

    private void PublishSessionActivity(string subtitle)
    {
        _published = _clock.Value;

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Pomodoro,
            Title = _clock.Value.ToString(@"mm\:ss", CultureInfo.InvariantCulture),
            Subtitle = subtitle,
            Source = "Pomodoro",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.Normal
        });
    }

    private void StopTimer() => _tickTimer.Change(Timeout.Infinite, Timeout.Infinite);
}
