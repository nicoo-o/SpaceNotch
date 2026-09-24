using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Scenes;

namespace SpaceNotch.SamplePlugin.Weather;

/// <summary>
/// Affiche la météo du lieu configuré, sous forme de carte dans l'Island.
///
/// Ce que cette fonctionnalité démontre, point par point :
/// <list type="bullet">
/// <item><b>Un cycle de vie symétrique.</b> <see cref="OnStartAsync"/> acquiert un
/// minuteur et un jeton d'annulation ; <see cref="OnStopAsync"/> libère exactement
/// les deux. Une bascule d'activation est donc rejouable sans accumuler les
/// minuteurs — la minutie qui, manquée, transforme un greffon en fuite ;</item>
/// <item><b>Un identifiant d'activité stable.</b> Republier sous le même
/// identifiant <em>remplace</em> le relevé précédent au lieu de l'empiler ;</item>
/// <item><b>Un rafraîchissement borné.</b> Un seul minuteur, une longue période,
/// désarmé à l'arrêt. C'est l'exception admise à la règle « aucun sondage » :
/// une donnée d'ambiance n'a pas d'événement pour la signaler ;</item>
/// <item><b>Aucune panne remontée à l'hôte.</b> Un réseau absent produit une
/// erreur signalée et un relevé conservé, jamais un échec de démarrage.</item>
/// </list>
/// </summary>
public sealed class WeatherFeature : IslandFeatureBase
{
    /// <summary>Identifiant de la fonctionnalité. Il est stable : il apparaît dans les diagnostics et les bascules.</summary>
    public const string FeatureKey = "plugin.weather";

    /// <summary>Identifiant de l'activité publiée. Stable lui aussi — c'est ce qui empêche l'accumulation.</summary>
    public const string ActivityId = "plugin.weather.current";

    public const string RefreshAction = "weather.refresh";

    public const string ToggleUnitAction = "weather.toggle-unit";

    /// <summary>
    /// Période de rafraîchissement. Quinze minutes : assez rare pour que le coût
    /// soit négligeable, assez fréquent pour que la valeur affichée reste juste.
    /// </summary>
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(15);

    private readonly IWeatherSource _source;
    private readonly WeatherLocation _location;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private Timer? _timer;
    private CancellationTokenSource? _lifetime;
    private Task _pendingRefresh = Task.CompletedTask;

    private WeatherSnapshot? _latest;
    private WeatherUnits _units = WeatherUnits.Celsius;

    public WeatherFeature(
        IActivityManager activities,
        SpaceNotch.Core.Events.IEventBus events,
        IWeatherSource source,
        WeatherLocation location,
        bool isEnabled = true)
        : base(FeatureKey, "Météo locale", activities, events, isEnabled)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _location = location ?? throw new ArgumentNullException(nameof(location));
    }

    /// <summary>Lieu observé, exposé pour les diagnostics et les tests.</summary>
    public WeatherLocation Location => _location;

    /// <summary>Dernier relevé obtenu, ou <c>null</c> si aucun n'a encore abouti.</summary>
    public WeatherSnapshot? Latest => _latest;

    /// <summary>Unité d'affichage courante.</summary>
    public WeatherUnits Units => _units;

    /// <summary>
    /// Tâche du rafraîchissement en cours ou du dernier déclenché.
    ///
    /// Elle existe parce que le premier relevé est lancé <em>sans être attendu</em>
    /// — voir <see cref="OnStartAsync"/> — et qu'un appelant doit pouvoir savoir
    /// quand la carte est à jour. Les tests s'en servent pour ne pas dépendre d'une
    /// temporisation arbitraire.
    /// </summary>
    public Task PendingRefresh => _pendingRefresh;

    /// <summary>
    /// Acquisition : un jeton d'annulation et un minuteur.
    ///
    /// <b>Le premier relevé n'est pas attendu, et c'est délibéré.</b> Le registre
    /// de l'hôte démarre les fonctionnalités en les attendant : attendre ici une
    /// réponse HTTP repousserait l'apparition de l'Island de la durée de l'appel,
    /// et un service lent la retarderait de plusieurs secondes. Le démarrage ne
    /// doit jamais dépendre d'un accès réseau.
    /// </summary>
    protected override Task OnStartAsync(CancellationToken cancellationToken)
    {
        _lifetime = new CancellationTokenSource();

        QueueRefresh();

        // Minuteur à longue période, désarmé à l'arrêt : c'est la seule forme de
        // travail périodique que le projet admet, parce qu'elle est bornée et
        // qu'aucun événement ne peut signaler un changement de temps.
        _timer = new Timer(
            _ => QueueRefresh(),
            state: null,
            dueTime: RefreshInterval,
            period: RefreshInterval);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Libération, strictement symétrique de l'acquisition.
    ///
    /// Le retrait des activités publiées n'est pas fait ici : la classe de base
    /// s'en charge à l'arrêt, ce qui garantit qu'une fonctionnalité arrêtée ne
    /// laisse aucune trace à l'écran même si sa propre libération échouait.
    /// </summary>
    protected override Task OnStopAsync()
    {
        _timer?.Dispose();
        _timer = null;

        CancellationTokenSource? lifetime = _lifetime;
        _lifetime = null;

        if (lifetime is not null)
        {
            try
            {
                lifetime.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Déjà libéré : rien à faire.
            }

            lifetime.Dispose();
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Relève et publie la météo.
    ///
    /// Séquencé par un sémaphore : deux déclenchements rapprochés — le minuteur et
    /// un clic sur « Actualiser » — ne produisent pas deux requêtes concurrentes.
    /// </summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        CancellationTokenSource? lifetime = _lifetime;

        if (lifetime is null)
        {
            return;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            lifetime.Token);

        await _refreshGate.WaitAsync(linked.Token).ConfigureAwait(false);

        try
        {
            // Pendant le relevé, la carte existante le dit par le mouvement
            // « Sync » : le greffon demande un préréglage, il ne dessine rien. Le
            // tout premier relevé n'a pas de carte à animer — il n'y a encore
            // rien à montrer.
            Publish(syncing: true);

            WeatherSnapshot snapshot = await _source
                .GetCurrentAsync(_location, linked.Token)
                .ConfigureAwait(false);

            _latest = snapshot;
            Publish();
        }
        catch (OperationCanceledException)
        {
            // Arrêt de la fonctionnalité : ce n'est pas un incident.
        }
        catch (Exception ex)
        {
            // Une panne réseau ne doit pas mettre la fonctionnalité en échec : le
            // relevé précédent reste affiché, et l'erreur est signalée à l'hôte.
            // La carte cesse de se synchroniser : un mouvement qui continuerait
            // après l'échec mentirait sur ce qui se passe.
            Publish();
            ReportError(ex);
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    /// <summary>
    /// Exécution des actions déclarées par la carte.
    ///
    /// La vue ne connaît que des identifiants ; c'est ici, et nulle part ailleurs,
    /// que leur signification existe.
    /// </summary>
    public override async Task<bool> HandleActionAsync(IslandActionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Le registre route déjà par activité propriétaire ; ce contrôle protège
        // d'un appel direct avec l'identifiant d'une autre fonctionnalité.
        if (!string.Equals(request.ActivityId, ActivityId, StringComparison.Ordinal))
        {
            return false;
        }

        switch (request.ActionId)
        {
            case RefreshAction:
                await RefreshAsync().ConfigureAwait(false);
                return true;

            case ToggleUnitAction:
                _units = _units == WeatherUnits.Celsius
                    ? WeatherUnits.Fahrenheit
                    : WeatherUnits.Celsius;

                // L'unité est une décision d'affichage : republier suffit, aucune
                // donnée n'a besoin d'être relue.
                Publish();
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Publie le relevé courant sous forme de carte.
    /// </summary>
    /// <param name="syncing">
    /// Vrai pendant un relevé : la carte porte le mouvement hypnotique « Sync ».
    /// </param>
    private void Publish(bool syncing = false)
    {
        WeatherSnapshot? snapshot = _latest;

        // Rien à publier si aucun relevé n'a abouti, ou si la fonctionnalité a été
        // arrêtée pendant l'appel : publier à cet instant ferait réapparaître une
        // carte que la classe de base venait de retirer.
        if (snapshot is null || _lifetime is null || _lifetime.IsCancellationRequested)
        {
            return;
        }

        double value = _units == WeatherUnits.Fahrenheit
            ? (snapshot.TemperatureCelsius * 9.0 / 5.0) + 32.0
            : snapshot.TemperatureCelsius;

        string unitLabel = _units == WeatherUnits.Fahrenheit ? "°F" : "°C";

        var activity = new IslandActivity
        {
            Id = ActivityId,
            FeatureId = Id,
            // La clé de scène est la carte générique : c'est elle qui accueille le
            // contenu dont l'hôte n'a pas de vue dédiée.
            SceneKey = IslandSceneCatalog.Card,
            Title = string.Create(
                CultureInfo.InvariantCulture,
                $"{value:0} {unitLabel} · {WeatherCodeMap.Describe(snapshot.WeatherCode)}"),
            Subtitle = string.Create(
                CultureInfo.InvariantCulture,
                $"{_location.Name} · relevé de {snapshot.ObservedAt.ToLocalTime():HH:mm}"),
            // Un glyphe littéral : le greffon apporte sa propre icône sans dépendre
            // d'une clé que l'hôte devrait connaître.
            IconKey = WeatherCodeMap.GlyphFor(snapshot.WeatherCode, snapshot.IsDay),
            Source = "Météo locale",

            // Information d'ambiance : elle ne doit jamais supplanter un appel, une
            // notification ou une lecture en cours.
            Priority = ActivityPriority.Background,

            // Le langage de mouvement commun : pendant un relevé, la carte se
            // synchronise ; le reste du temps, elle ne bouge pas.
            MotionState = syncing ? ActivityMotionState.Working : ActivityMotionState.Idle,
            MotionPreset = HypnoticPreset.Sync,

            Actions =
            [
                new ActivityAction(
                    RefreshAction,
                    "Actualiser",
                    "\uE72C",
                    ActivityActionKind.Invoke,
                    IsPrimary: true),
                new ActivityAction(
                    ToggleUnitAction,
                    _units == WeatherUnits.Fahrenheit ? "En °C" : "En °F",
                    "\uECC6")
            ]
        };

        PublishActivity(activity);
    }

    /// <summary>
    /// Demande un rafraîchissement sans l'attendre, en mémorisant la tâche.
    ///
    /// L'affectation de <see cref="_pendingRefresh"/> est synchrone : lorsque
    /// <see cref="OnStartAsync"/> rend la main, la tâche du premier relevé est déjà
    /// observable. Un appelant peut donc l'attendre sans risquer d'attendre une
    /// tâche plus ancienne.
    /// </summary>
    private void QueueRefresh()
    {
        CancellationTokenSource? lifetime = _lifetime;

        if (lifetime is null || lifetime.IsCancellationRequested)
        {
            return;
        }

        _pendingRefresh = RefreshAsync(lifetime.Token);
    }

    /// <summary>
    /// Libération finale. La source est libérée si elle le souhaite — c'est le cas
    /// de l'implémentation réelle, qui détient un transport HTTP. Un greffon qui
    /// crée lui-même ses ressources doit les libérer lui-même.
    /// </summary>
    protected override void OnDisposed()
    {
        if (_source is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _refreshGate.Dispose();
    }
}
