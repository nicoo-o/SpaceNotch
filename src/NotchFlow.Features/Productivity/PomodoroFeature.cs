using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using NotchFlow.Core.Activities;
using NotchFlow.Core.Events;
using NotchFlow.Core.Features;
using NotchFlow.Core.Scenes;
using NotchFlow.Core.State;

namespace NotchFlow.Features.Productivity;

/// <summary>
/// Session de travail minutée.
///
/// Exception documentée à la règle « aucune boucle » : un compte à rebours doit
/// avancer, donc il bat à 1 Hz. Quatre garde-fous le maintiennent dans un budget
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

    private TimeSpan _remaining = DefaultSessionLength;
    private bool _sessionRunning;

    public PomodoroFeature(
        IActivityManager activities,
        IEventBus events,
        bool isEnabled = true)
        : base(FeatureKey, "Minuteur de focus", activities, events, isEnabled)
    {
        // Créé suspendu : une fonctionnalité au repos ne consomme rien.
        _tickTimer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsSessionRunning => _sessionRunning;

    public TimeSpan Remaining => _remaining;

    public void Start(TimeSpan? duration = null)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (duration.HasValue)
        {
            _remaining = duration.Value;
        }

        if (_sessionRunning)
        {
            return;
        }

        _sessionRunning = true;
        _tickTimer.Change(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        PublishSessionActivity("Focus en cours");
    }

    public void Pause()
    {
        if (!_sessionRunning)
        {
            return;
        }

        _sessionRunning = false;
        StopTimer();
        PublishSessionActivity("En pause");
    }

    public void Reset(TimeSpan? duration = null)
    {
        _sessionRunning = false;
        StopTimer();
        _remaining = duration ?? DefaultSessionLength;
        RemoveActivity(ActivityId);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        // Désactiver la fonctionnalité interrompt la session : le minuteur ne doit
        // pas continuer à battre pour une fonctionnalité éteinte.
        _sessionRunning = false;
        StopTimer();
        _remaining = DefaultSessionLength;

        return Task.CompletedTask;
    }

    protected override void OnDisposed() => _tickTimer.Dispose();

    private void OnTick(object? state)
    {
        if (!_sessionRunning)
        {
            return;
        }

        if (_remaining > TimeSpan.Zero)
        {
            _remaining -= TimeSpan.FromSeconds(1);

            // Même identifiant : l'activité est remplacée, pas empilée.
            PublishSessionActivity("Focus en cours");
            return;
        }

        _sessionRunning = false;
        StopTimer();

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Pomodoro,
            Title = "Session terminée",
            Subtitle = "Prenez une pause",
            Source = "Pomodoro",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.High,
            Duration = TimeSpan.FromSeconds(5)
        });

        PublishEvent(new NotificationPostedEvent("Pomodoro", "Session terminée", "Prenez une pause"));
    }

    private void PublishSessionActivity(string subtitle)
    {
        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Pomodoro,
            Title = _remaining.ToString(@"mm\:ss", CultureInfo.InvariantCulture),
            Subtitle = subtitle,
            Source = "Pomodoro",
            IconKey = "Timer",
            State = IslandActivityState.TimerActive,
            Priority = ActivityPriority.Normal
        });
    }

    private void StopTimer() => _tickTimer.Change(Timeout.Infinite, Timeout.Infinite);
}
