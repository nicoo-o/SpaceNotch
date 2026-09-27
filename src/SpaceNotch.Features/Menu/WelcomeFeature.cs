using System;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Core.Localization;

namespace SpaceNotch.Features.Menu;

/// <summary>
/// Présentation du premier lancement : la notch s'ouvre d'elle-même sur cinq
/// cartes — survol et clic, clic droit, Alt+Espace, détacher, notifications.
/// La fonctionnalité tient l'étape ; la fenêtre décide quand la montrer et
/// exécute « Autoriser » (la demande d'accès doit partir du fil d'interface).
/// </summary>
public sealed class WelcomeFeature : IslandFeatureBase
{
    public const string FeatureKey = "feature.welcome";
    public const string ActivityId = "feature.welcome.current";

    public const string NextAction = "welcome.next";
    public const string SkipAction = "welcome.skip";
    public const string AllowAction = "welcome.allow";

    public const int StepCount = 5;

    private int? _step;
    private string _access = "unavailable";

    public WelcomeFeature(IActivityManager activities, IEventBus events)
        : base(FeatureKey, "Présentation", activities, events, isEnabled: true)
    {
    }

    public bool IsShown => _step is not null;

    /// <summary>Levé quand la présentation se termine, vue jusqu'au bout ou passée.</summary>
    public event EventHandler? Completed;

    /// <summary>Montre la première carte. <paramref name="notificationAccess"/> adapte la dernière.</summary>
    public void Show(string notificationAccess)
    {
        _access = notificationAccess;
        _step = 0;
        Publish();
    }

    /// <summary>Met à jour l'état des notifications (après « Autoriser ») sans changer d'étape.</summary>
    public void UpdateAccess(string notificationAccess)
    {
        _access = notificationAccess;

        if (_step is not null)
        {
            Publish();
        }
    }

    public void Finish()
    {
        if (_step is null)
        {
            return;
        }

        _step = null;
        RemoveActivity(ActivityId);
        Completed?.Invoke(this, EventArgs.Empty);
    }

    protected override Task OnStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    protected override Task OnStopAsync()
    {
        _step = null;
        RemoveActivity(ActivityId);
        return Task.CompletedTask;
    }

    public override Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        switch (request.ActionId)
        {
            case NextAction when _step is int step:
                if (step >= StepCount - 1)
                {
                    Finish();
                }
                else
                {
                    _step = step + 1;
                    Publish();
                }

                return Task.FromResult(true);

            case SkipAction:
                Finish();
                return Task.FromResult(true);

            default:
                return Task.FromResult(false);
        }
    }

    private void Publish()
    {
        if (_step is not int step)
        {
            return;
        }

        PublishActivity(new IslandActivity
        {
            Id = ActivityId,
            FeatureId = FeatureKey,
            SceneKey = IslandSceneCatalog.Welcome,
            Title = Lang.T("Bienvenue", "Welcome"),
            Source = "SpaceNotch",
            IconKey = "Welcome",
            State = IslandActivityState.Idle,
            Priority = ActivityPriority.Normal,
            Payload = new WelcomePayload(step, StepCount, _access)
        });
    }
}
