using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Events;

namespace SpaceNotch.Core.Features;

/// <summary>
/// Base commune des fonctionnalités.
///
/// Elle concentre les comportements que chaque fonctionnalité devrait sinon
/// réimplémenter, souvent de travers :
/// <list type="bullet">
/// <item>le cycle de vie est idempotent — démarrer deux fois ne réabonne pas
/// deux fois aux événements système, ce qui produirait des doublons dans
/// l'Island et une fuite d'abonnements ;</item>
/// <item>l'échec d'activation est isolé — l'indisponibilité d'une API système
/// ne doit pas empêcher l'Island de fonctionner ;</item>
/// <item>la désactivation libère réellement les ressources et retire les
/// activités publiées, au lieu de laisser des entrées orphelines.</item>
/// </list>
///
/// Les abonnements aux événements plateforme se font dans
/// <see cref="OnStartAsync"/> et se défont dans <see cref="OnStopAsync"/> : c'est
/// cette symétrie qui rend une bascule d'activation rejouable.
/// </summary>
public abstract class IslandFeatureBase : IIslandFeature
{
    private readonly IActivityManager _activities;
    private readonly IEventBus _events;
    private bool _disposed;

    protected IslandFeatureBase(
        string id,
        string displayName,
        IActivityManager activities,
        IEventBus events,
        bool isEnabled = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        Id = id;
        DisplayName = displayName;
        _activities = activities ?? throw new ArgumentNullException(nameof(activities));
        _events = events ?? throw new ArgumentNullException(nameof(events));
        IsEnabled = isEnabled;
    }

    public string Id { get; }

    public string DisplayName { get; }

    public FeatureState State { get; private set; } = FeatureState.Stopped;

    public bool IsEnabled { get; private set; }

    public Exception? LastError { get; private set; }

    public event EventHandler<FeatureStateChangedEventArgs>? StateChanged;

    protected IActivityManager Activities => _activities;

    protected IEventBus Events => _events;

    /// <summary>Vrai tant que la fonctionnalité est active et n'a pas échoué.</summary>
    public bool IsRunning => State == FeatureState.Running;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || !IsEnabled || State is FeatureState.Running or FeatureState.Starting)
        {
            return;
        }

        TransitionTo(FeatureState.Starting, error: null);

        try
        {
            await OnStartAsync(cancellationToken).ConfigureAwait(false);
            TransitionTo(FeatureState.Running, error: null);
        }
        catch (Exception ex)
        {
            // Isolation : une fonctionnalité en échec ne doit jamais empêcher
            // l'Island de démarrer.
            TransitionTo(FeatureState.Faulted, ex);
        }
    }

    public async Task StopAsync()
    {
        if (State is FeatureState.Stopped or FeatureState.Stopping)
        {
            return;
        }

        TransitionTo(FeatureState.Stopping, LastError);

        try
        {
            await OnStopAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // L'erreur d'arrêt est consignée : elle n'empêche pas de considérer la
            // fonctionnalité comme arrêtée, mais elle doit rester consultable.
            LastError = ex;
        }
        finally
        {
            // Une fonctionnalité arrêtée ne laisse aucune activité derrière elle :
            // « inactive = zéro travail » vaut aussi pour les données.
            _activities.RemoveActivitiesFrom(Id);
            TransitionTo(FeatureState.Stopped, LastError);
        }
    }

    public async Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default)
    {
        if (IsEnabled == enabled)
        {
            return;
        }

        IsEnabled = enabled;

        if (enabled)
        {
            await StartAsync(cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await StopAsync().ConfigureAwait(false);
        }
    }

    public IEnumerable<IslandActivity> GetActivities()
        => _activities.GetActiveActivities()
            .Where(a => string.Equals(a.FeatureId, Id, StringComparison.Ordinal));

    /// <summary>
    /// Publie une activité et le signal correspondant sur le bus. Les
    /// fonctionnalités n'ont à orchestrer ni retrait ni expiration : le
    /// gestionnaire d'activités en est propriétaire.
    /// </summary>
    protected void PublishActivity(IslandActivity activity)
    {
        _activities.PostActivity(activity);

        _events.Publish(new ActivityStartedEvent(activity.Id, activity.FeatureId, activity.SceneKey));
    }

    /// <summary>Retire une activité publiée par cette fonctionnalité.</summary>
    protected bool RemoveActivity(string activityId) => _activities.RemoveActivity(activityId);

    /// <summary>Publie un événement de domaine sur le bus interne.</summary>
    protected void PublishEvent<TEvent>(TEvent @event) => _events.Publish(@event);

    /// <summary>
    /// Traitement optionnel d'un message de fenêtre. Permet à une fonctionnalité
    /// de réagir à une notification système diffusée par message plutôt que de
    /// recourir à un sondage.
    /// </summary>
    /// <returns><c>true</c> si le message a été consommé.</returns>
    public virtual bool TryHandleWindowMessage(uint messageId, nuint wParam) => false;

    /// <summary>
    /// Exécution d'une action déclarée. Par défaut, la fonctionnalité n'expose
    /// aucun contrôle ; une fonctionnalité qui en déclare doit surcharger cette
    /// méthode, faute de quoi l'Island afficherait des boutons inertes.
    /// </summary>
    public virtual Task<bool> HandleActionAsync(IslandActionRequest request) => Task.FromResult(false);

    /// <summary>Acquisition des écouteurs système. Appelée uniquement au démarrage.</summary>
    protected abstract Task OnStartAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Libération des écouteurs système. Doit être strictement symétrique de
    /// <see cref="OnStartAsync"/>, faute de quoi une réactivation produirait des
    /// abonnements en double.
    /// </summary>
    protected abstract Task OnStopAsync();

    /// <summary>
    /// Signalé pour une erreur non fatale, sans changement d'état.
    ///
    /// Une fonctionnalité peut continuer à fonctionner après un échec partiel —
    /// un dossier non observable, une application impossible à lancer. Le
    /// signaler sans la déclarer en panne évite deux écueils : rendre la
    /// fonctionnalité inutilisable pour un incident mineur, ou le taire.
    /// </summary>
    public event EventHandler<Exception>? ErrorReported;

    /// <summary>
    /// Consigne une erreur non fatale et la porte à la connaissance de l'hôte.
    /// </summary>
    protected void ReportError(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        LastError = exception;
        ErrorReported?.Invoke(this, exception);
    }

    /// <summary>Libération finale, une seule fois pour la vie de l'objet.</summary>
    protected virtual void OnDisposed()
    {
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        await StopAsync().ConfigureAwait(false);
        OnDisposed();
        StateChanged = null;
        ErrorReported = null;

        GC.SuppressFinalize(this);
    }

    private void TransitionTo(FeatureState newState, Exception? error)
    {
        FeatureState oldState = State;

        if (oldState == newState)
        {
            return;
        }

        State = newState;

        if (error is not null)
        {
            LastError = error;
        }

        StateChanged?.Invoke(this, new FeatureStateChangedEventArgs(Id, oldState, newState, error));
    }
}
