using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SpaceNotch.Core.Activities;
using SpaceNotch.Core.Animation;
using SpaceNotch.Core.Events;
using SpaceNotch.Core.Features;
using SpaceNotch.Core.Motion;
using SpaceNotch.Core.Presentation;
using SpaceNotch.Core.Scenes;
using SpaceNotch.Core.State;
using SpaceNotch.Features.Bluetooth;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Features.FileShelf;
using SpaceNotch.Features.Launcher;
using SpaceNotch.Features.Media;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.Productivity;
using SpaceNotch.Features.SystemHud;
using SpaceNotch.Infrastructure.Config;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Infrastructure.Plugins;
using SpaceNotch.Platform.Windows.Audio;
using SpaceNotch.Platform.Windows.Bluetooth;
using SpaceNotch.Platform.Windows.Clipboard;
using SpaceNotch.Platform.Windows.Display;
using SpaceNotch.Platform.Windows.Media;
using SpaceNotch.Platform.Windows.Notifications;
using SpaceNotch.Platform.Windows.Shell;
using SpaceNotch.Platform.Windows.System;
using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Controllers;
using SpaceNotch_App.Diagnostics;
using SpaceNotch_App.Views;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;
using WinUIEx.Messaging;

namespace SpaceNotch_App.Windows;

/// <summary>
/// Surface interactive de l'Island.
///
/// Elle ne décide de rien : elle résout la clé de scène déclarée par la
/// fonctionnalité en une vue, applique l'encombrement que le contrôleur lui
/// transmet, et convertit les DIPs en pixels physiques avec l'échelle du
/// moniteur cible. Aucune chaîne de conditions sur le type de contenu reçu, et
/// aucune dimension codée en dur.
/// </summary>
public sealed partial class IslandWindow : Window
{
    private readonly IntPtr _hWnd;
    private readonly AppWindow _appWindow;
    private readonly DispatcherQueue _dispatcherQueue;
    /// <summary>
    /// Source unique des préférences. La fenêtre ne détient pas de copie qui
    /// pourrait diverger de celle de la fenêtre de réglages.
    /// </summary>
    private readonly SettingsService _settingsService;

    /// <summary>
    /// Instantané courant des préférences. Réassigné à chaque changement — la
    /// réinitialisation remplace l'instance — et lu en place le reste du temps.
    /// </summary>
    private AppSettings _settings;

    private readonly IslandStateManager _stateManager = new();
    private readonly ActivityManager _activityManager = new();
    private readonly EventBus _eventBus = new();
    private readonly IslandController _controller;
    private readonly ScreenChangeWatcher _screenWatcher = new();
    private readonly FullscreenPresenceWatcher _presence = new();
    private readonly RuntimeDiagnostics _diagnostics;
    private readonly AtmosphereWindow _atmosphere;

    // Services plateforme
    private readonly WindowsMediaSessionManager _mediaSessionManager = new();
    private readonly CoreAudioVolumeListener _volumeListener = new();
    private readonly WindowsNotificationListener _notificationListener = new();
    private readonly BluetoothWatcher _bluetoothWatcher = new();
    private readonly ClipboardMonitor _clipboardMonitor = new();
    private readonly BrightnessService _brightnessService = new();

    // Fonctionnalités : le registre en est propriétaire. La fenêtre n'en garde que
    // les références dont elle a besoin pour ses propres interactions.
    private readonly FileShelfManager _shelfManager;
    private readonly PomodoroFeature _pomodoroFeature;
    private readonly TimerFeature _timerFeature;
    private readonly LauncherFeature _launcherFeature;
    private readonly MediaFeature _mediaFeature;
    private readonly IslandFeatureRegistry _featureRegistry;
    private readonly PluginLoader _pluginLoader;

    private readonly Dictionary<string, IIslandSceneView> _scenes = new(StringComparer.Ordinal);

    /// <summary>
    /// Vrai lorsqu'un rendu attend déjà sur le fil d'interface. Écrit et lu par
    /// plusieurs fils, d'où l'échange atomique.
    /// </summary>
    private int _renderPending;

    /// <summary>
    /// Clés de scène déjà signalées comme inconnues. Évite de journaliser à chaque
    /// republication la même erreur d'une fonctionnalité mal configurée.
    /// </summary>
    private readonly HashSet<string> _reportedUnknownScenes = new(StringComparer.Ordinal);
    private readonly List<FrameworkElement> _sceneRoots = [];

    /// <summary>Tracés de la silhouette et du reflet, mémorisés entre deux images.</summary>
    private readonly IslandGeometryFactory _shape = new();

    /// <summary>Hauteur de la bande de reflet, lue une fois dans les jetons.</summary>
    private double _specularHeight = 22;

    /// <summary>Palier de présentation présenté. Sert à savoir lequel annoncer au survol.</summary>
    private IslandPresentationTier _tier = IslandPresentationTier.Idle;

    /// <summary>Épaule appliquée en dernier à la zone de contenu, pour ne la redisposer qu'au changement.</summary>
    private double _contentShoulder = double.NaN;

    /// <summary>
    /// Matière hypnotique des paliers signal et carte, et de la cible de dépôt.
    /// <c>null</c> si le compositeur l'a refusée : le glyphe fixe reste alors.
    /// </summary>
    private HypnoticSurface? _signalHypnotic;
    private HypnoticSurface? _cardHypnotic;
    private HypnoticSurface? _dropHypnotic;

    /// <summary>Mouvement confié à l'atmosphère, pour ne relancer sa respiration qu'au changement.</summary>
    private HypnoticPreset _atmospherePreset = HypnoticPreset.None;

    /// <summary>
    /// Vrai pendant l'achèvement d'un dépôt : la matière converge et pulse, et
    /// le rendu attend la fin de ce geste avant de montrer l'étagère.
    /// </summary>
    private bool _dropCompleting;

    /// <summary>Filet de sécurité : l'achèvement d'un dépôt ne peut pas bloquer la notch.</summary>
    private DispatcherQueueTimer? _dropCompletionTimer;

    private WindowMessageMonitor? _messageMonitor;
    private DispatcherQueueTimer? _geometryTimer;
    private DispatcherQueueTimer? _expirationTimer;

    /// <summary>Fermeture différée de l'aperçu de survol.</summary>
    private DispatcherQueueTimer? _previewExitTimer;

    /// <summary>Ouverture différée de l'aperçu : l'intention de survol.</summary>
    private DispatcherQueueTimer? _previewEnterTimer;

    private SettingsWindow? _settingsWindow;

    /// <summary>
    /// Satellite détaché, créé à la demande.
    ///
    /// Il n'existe pas tant qu'une seconde activité n'est pas apparue : une
    /// fenêtre supplémentaire au démarrage coûterait de la mémoire et un ordre de
    /// superposition à défendre pour un cas qui ne se produit pas toujours.
    /// </summary>
    private SatelliteWindow? _satellite;

    private SystemVisualState _visualState = SystemVisualState.Permissive;
    private bool _isClosed;

    // Dernier rectangle physique réellement soumis au gestionnaire de fenêtres.
    // Sert uniquement à ne pas le resoumettre à l'identique (voir ApplyGeometry).
    private int _lastWindowX = int.MinValue;
    private int _lastWindowY = int.MinValue;
    private int _lastWindowWidth = int.MinValue;
    private int _lastWindowHeight = int.MinValue;

    public IslandWindow()
    {
        InitializeComponent();

        _dispatcherQueue = DispatcherQueue;

        // La configuration est lue avant toute création de fenêtre : c'est elle
        // qui détermine la géométrie, le moniteur cible et le mode de fond.
        // Une configuration illisible ou non enregistrable est tolérable, mais pas
        // silencieuse : sans ce branchement, une préférence perdue serait
        // indiscernable d'une régression.
        _settingsService = new SettingsService();
        _settingsService.ReadFailed = (path, ex) => MiniLogger.Log($"[CONFIG] lecture impossible : {path}", ex);
        _settingsService.WriteFailed = (path, ex) => MiniLogger.Log($"[CONFIG] écriture impossible : {path}", ex);

        _settings = _settingsService.Current;

        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);

        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.SetBorderAndTitleBar(false, false);
            presenter.IsAlwaysOnTop = true;
            presenter.IsResizable = false;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
        }

        // Sort de la barre des tâches et d'Alt+Tab, et ne prend jamais le focus.
        WindowChrome.ApplyInteractiveSurface(_hWnd);

        _visualState = SystemVisualState.Read();

        // La couche décorative est créée avant le contrôleur : celui-ci applique
        // sa géométrie initiale dès sa construction (SnapTo), et la géométrie
        // doit pouvoir positionner les deux surfaces.
        _atmosphere = new AtmosphereWindow();

        // La préférence est lue par clé de fonctionnalité : la fenêtre n'associe
        // donc pas elle-même une fonctionnalité à un réglage, elle demande.
        _shelfManager = new FileShelfManager(
            _activityManager, _eventBus, _settings.IsFeatureEnabled(FileShelfManager.FeatureKey));

        _pomodoroFeature = new PomodoroFeature(
            _activityManager, _eventBus, _settings.IsFeatureEnabled(PomodoroFeature.FeatureKey));

        _timerFeature = new TimerFeature(_activityManager, _eventBus);
        _launcherFeature = new LauncherFeature(_activityManager, _eventBus);

        _mediaFeature = new MediaFeature(
            _activityManager, _eventBus, _mediaSessionManager, _settings.IsFeatureEnabled(MediaFeature.FeatureKey));

        // Le registre remplace le câblage ad hoc : démarrage isolé, arrêt
        // symétrique, routage des messages et bascules d'activation effectives.
        var features = new List<IIslandFeature>
        {
            _mediaFeature,
            new SystemHudFeature(
                _activityManager, _eventBus, _volumeListener,
                _settings.IsFeatureEnabled(SystemHudFeature.FeatureKey)),
            new NotificationFeature(
                _activityManager, _eventBus, _notificationListener,
                _settings.IsFeatureEnabled(NotificationFeature.FeatureKey)),
            new BluetoothFeature(
                _activityManager, _eventBus, _bluetoothWatcher,
                _settings.IsFeatureEnabled(BluetoothFeature.FeatureKey)),
            _pomodoroFeature,
            _shelfManager,
            new BrightnessHudFeature(
                _activityManager, _eventBus, _brightnessService,
                _settings.IsFeatureEnabled(BrightnessHudFeature.FeatureKey)),
            _timerFeature,
            _launcherFeature,
            new ClipboardFeature(
                _activityManager, _eventBus, _clipboardMonitor, _hWnd,
                _settings.IsFeatureEnabled(ClipboardFeature.FeatureKey))
        };

        // Les greffons sont chargés avant la création du registre : ils en font
        // partie dès le démarrage et bénéficient donc exactement du même cycle de
        // vie, des mêmes bascules et du même routage d'actions que les
        // fonctionnalités intégrées.
        _pluginLoader = new PluginLoader();

        PluginLoadResult plugins = _pluginLoader.LoadAll(
            new IslandFeatureContext(_activityManager, _eventBus));

        features.AddRange(plugins.Features);

        foreach (string failure in plugins.Failures)
        {
            MiniLogger.Log($"[PLUGIN] {failure}");
        }

        MiniLogger.Log($"Greffons chargés : {plugins.Features.Count} fonctionnalité(s), {plugins.Failures.Count} échec(s)");

        _featureRegistry = new IslandFeatureRegistry(
            features,
            onFault: (featureId, exception) => MiniLogger.Log($"[FEATURE] {featureId} en échec", exception));

        _controller = new IslandController(
            _stateManager,
            _activityManager,
            _settings.Motion,
            _settings.HoverMotion,
            IslandFootprint.For(IslandPresentation.Resolve(null), _settings.Density),
            UseSpringAnimations,
            ApplyGeometry);

        // Le contrôleur reçoit de quoi rejoindre le fil d'interface. Il en a besoin
        // dès maintenant : il est déjà abonné aux activités, et une publication
        // reçue avant que les fonctionnalités ne démarrent serait réagie hors fil —
        // or réagir peut animer, et animer n'existe que sur ce fil.
        _controller.SetDispatcher(OnUiThread);

        // Le survol annonce le palier du dessus : la politique vit ici, où le
        // palier présenté est connu, et non dans la mécanique du ressort.
        _controller.PreviewFootprint = ResolvePreviewFootprint;

        _diagnostics = new RuntimeDiagnostics(_activityManager, _stateManager, _controller);

        RegisterScenes();
        AttachHypnoticSurfaces();
        WireEvents();
        WireSceneActions();
        ApplyBackdropMode();

        // Toute modification venue de la fenêtre de réglages est appliquée ici,
        // par le même chemin que les préférences du menu : il n'existe donc pas
        // deux manières d'appliquer un réglage.
        _settingsService.Changed += OnSettingsChanged;

        SetupTrayIcon();

        _specularHeight = ResolveSpecularHeight();

        // Géométrie initiale : appliquée sans animation, l'Island démarre au repos.
        ApplyGeometry(_controller.CurrentFootprint);

        _appWindow.Show();
        _atmosphere.PlaceBehind(_hWnd);
        _atmosphere.UseSpringAnimations = UseSpringAnimations();

        // L'Island démarre dans l'état que la session impose : si une application
        // occupe déjà l'écran au lancement, elle n'apparaît pas du tout.
        ApplyPresence();

        RootLayout.Loaded += OnRootLoaded;
    }

    /// <summary>Le ressort n'est utilisé que si Windows l'autorise.</summary>
    private bool UseSpringAnimations()
        => _visualState.UseSpringAnimations && _settings.AllowBouncyAnimations;

    // ------------------------------------------------------------------
    // Matière hypnotique
    // ------------------------------------------------------------------

    /// <summary>
    /// Attache la matière hypnotique aux emplacements des glyphes. Rien ne
    /// tourne tant qu'aucune activité ne travaille : les surfaces naissent
    /// masquées et arrêtées.
    /// </summary>
    private void AttachHypnoticSurfaces()
    {
        _signalHypnotic = HypnoticSurface.TryAttach(SignalHypnoticHost);
        _cardHypnotic = HypnoticSurface.TryAttach(CardHypnoticHost);
        _dropHypnotic = HypnoticSurface.TryAttach(DropHypnoticHost);

        if (_dropHypnotic is not null)
        {
            _dropHypnotic.OneShotCompleted += (_, preset) =>
            {
                if (preset == HypnoticPreset.Complete)
                {
                    OnUiThread(FinishDrop);
                }
            };
        }
    }

    /// <summary>
    /// Le mouvement hypnotique est-il joué ? Sous réduction des animations, ou
    /// si l'utilisateur l'a éteint, la composition est posée fixe.
    /// </summary>
    private bool AnimateHypnotic() => UseSpringAnimations() && _settings.AllowHypnoticMotion;

    /// <summary>
    /// Donne à un emplacement de glyphe sa matière hypnotique, ou son glyphe.
    /// Jamais les deux : la matière remplace l'icône, elle ne s'y superpose pas.
    /// </summary>
    private void ApplyHypnoticSlot(
        HypnoticSurface? surface,
        FrameworkElement host,
        FrameworkElement glyph,
        HypnoticPreset preset,
        Color tint)
    {
        bool hypnotic = surface is not null && preset != HypnoticPreset.None;

        host.Visibility = hypnotic ? Visibility.Visible : Visibility.Collapsed;
        glyph.Visibility = hypnotic ? Visibility.Collapsed : Visibility.Visible;

        if (surface is null)
        {
            return;
        }

        surface.SetTint(tint);
        surface.SetPreset(hypnotic ? preset : HypnoticPreset.None, AnimateHypnotic());
    }

    /// <summary>Arrête la matière des paliers de repos, qui ne sont plus visibles.</summary>
    private void StopRestingHypnotic()
    {
        _signalHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        _cardHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
    }

    /// <summary>
    /// Teinte de la matière : celle de l'état s'il en porte une, sinon la
    /// lumière chaude de la référence — une source neutre et blanche se lirait
    /// comme un voyant, pas comme une matière qui travaille.
    /// </summary>
    private static Color HypnoticTint(IslandActivity activity)
    {
        if (activity.MotionState == ActivityMotionState.Error)
        {
            return Color.FromArgb(0xFF, 0xF0, 0x83, 0x6B);
        }

        if (activity.State is IslandActivityState.Idle or IslandActivityState.SystemHud)
        {
            return WarmHypnoticTint();
        }

        return StatePalette.Tint(activity.State);
    }

    private static Color WarmHypnoticTint()
        => Application.Current?.Resources?.TryGetValue("NfHypnoticWarmColor", out object? value) == true
            && value is Color color
                ? color
                : Color.FromArgb(0xFF, 0xFF, 0xB4, 0x6A);

    // ------------------------------------------------------------------
    // Résolution des scènes
    // ------------------------------------------------------------------

    /// <summary>
    /// Table clé de scène → vue. Une clé inconnue retombe sur la scène générique
    /// plutôt que de faire disparaître l'Island : une fonctionnalité nouvelle
    /// s'affiche donc sans modifier la fenêtre.
    /// </summary>
    private void RegisterScenes()
    {
        _scenes[IslandSceneCatalog.Media] = MediaSceneView;
        _scenes[IslandSceneCatalog.VolumeHud] = VolumeSceneView;
        _scenes[IslandSceneCatalog.Notification] = NotificationSceneView;
        _scenes[IslandSceneCatalog.FileShelf] = FileShelfSceneView;
        _scenes[IslandSceneCatalog.Timer] = TimerSceneView;
        _scenes[IslandSceneCatalog.Pomodoro] = TimerSceneView;
        _scenes[IslandSceneCatalog.Clipboard] = ClipboardSceneView;
        _scenes[IslandSceneCatalog.Launcher] = LauncherSceneView;

        // Luminosité et volume partagent la même vue : leur charge utile est
        // identique, seule la clé d'icône les distingue.
        _scenes[IslandSceneCatalog.BrightnessHud] = VolumeSceneView;

        // Scènes sans vue dédiée : elles partagent la vue générique, qui rend le
        // titre, le sous-titre et les contrôles déclarés. « Card » est la clé
        // destinée au contenu tiers — c'est par elle qu'un greffon s'affiche sans
        // que la fenêtre ait à le connaître.
        foreach (string fallbackKey in new[]
                 {
                     IslandSceneCatalog.Bluetooth,
                     IslandSceneCatalog.DropZone,
                     IslandSceneCatalog.Card
                 })
        {
            _scenes[fallbackKey] = InfoSceneView;
        }

        _sceneRoots.AddRange(_scenes.Values.Distinct().Select(scene => scene.Root));

        foreach (FrameworkElement root in _sceneRoots)
        {
            root.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Abonne la fenêtre aux demandes d'action émises par les scènes.
    ///
    /// La scène ne connaît qu'un identifiant ; c'est ici que la demande est
    /// acheminée vers la fonctionnalité propriétaire, seule capable de l'exécuter.
    /// </summary>
    private void WireSceneActions()
    {
        foreach (IIslandSceneView scene in _scenes.Values.Distinct())
        {
            scene.ActionRequested += OnSceneActionRequested;
        }
    }

    private async void OnSceneActionRequested(object? sender, IslandActionRequest request)
    {
        try
        {
            _diagnostics.CountEvent();

            bool handled = await _featureRegistry.HandleActionAsync(request);

            if (!handled)
            {
                MiniLogger.Log($"[ACTION] aucune fonctionnalité n'a traité {request.ActionId}");
                return;
            }

            // Une action qui a modifié l'affichage doit être reprojetée : la
            // fonctionnalité a pu republier son activité, mais elle a aussi pu se
            // contenter d'agir — le rendu est alors rafraîchi sans attendre.
            Render();
        }
        catch (Exception ex)
        {
            MiniLogger.Log($"[ACTION] échec de {request.ActionId}", ex);
        }
    }

    private void WireEvents()
    {
        // Ces signaux peuvent provenir de n'importe quel fil. Les fonctionnalités
        // intégrées publient depuis des rappels que Windows marshale déjà — les
        // messages de fenêtre, les événements de session média — mais rien ne
        // l'impose : un greffon qui publie depuis un fil de travail emprunte le même
        // chemin sans répartition. Et il ne peut pas s'en défendre seul : son
        // contexte ne contient aucun répartiteur, par choix. Le fil est donc rétabli
        // ici, du côté qui possède l'interface.
        //
        // Sans cela, l'appel atteint un minuteur de file d'attente depuis le mauvais
        // fil et lève un COMException au message vide — une panne d'autant plus
        // difficile à lire que rien ne la désigne.
        _controller.PresentedActivityChanged += (_, _) => RequestRender();
        _controller.AnimationCompleted += (_, _) => RequestRender();
        _controller.StateChanged += (_, _) => RequestRender();

        _activityManager.ActiveActivityChanged += (_, _) => OnUiThread(RearmExpirationTimer);
        _activityManager.ActivityRemoved += (_, _) => OnUiThread(RearmExpirationTimer);

        // Les défaillances d'abonnés ne sont plus silencieuses.
        _eventBus.HandlerFailed = (eventType, exception) =>
            MiniLogger.Log($"[BUS] Abonné défaillant pour {eventType.Name}", exception);

        _screenWatcher.Changed += OnEnvironmentChanged;

        // Le shell signale lui-même le changement d'occupation de l'écran :
        // l'Island se retire sans qu'aucune scrutation n'ait lieu.
        // L'Island dit où elle se trouve, le veilleur dit si une autre fenêtre
        // occupe cette place. Sans ce rectangle, un navigateur agrandi laisserait
        // l'Island posée sur sa barre d'onglets.
        _presence.IslandBounds = () => (_lastWindowX, _lastWindowY, _lastWindowWidth, _lastWindowHeight);

        _presence.Changed += (_, _) => ApplyPresence();

        _messageMonitor = new WindowMessageMonitor(_hWnd);
        _messageMonitor.WindowMessageReceived += OnWindowMessageReceived;

        _shelfManager.ShelfUpdated += (_, _) =>
            OnUiThread(() => FileShelfSceneView.UpdateItems(_shelfManager.GetItems()));

        // Les contrôles de la scène média ne sont plus câblés ici : la scène
        // déclare ses actions, la fenêtre les route vers la fonctionnalité
        // propriétaire. La fenêtre n'appelle donc plus directement la session
        // média, ce qui était exactement le couplage qu'il fallait retirer.
        NotificationSceneView.DismissRequested += () => _controller.RequestCollapse();

        Closed += OnWindowClosed;
    }

    // ------------------------------------------------------------------
    // Démarrage
    // ------------------------------------------------------------------

    private async void OnRootLoaded(object sender, RoutedEventArgs e)
    {
        MiniLogger.Log("IslandWindow chargée : démarrage des fonctionnalités");

        // Le registre démarre les fonctionnalités activées en isolant les échecs :
        // l'indisponibilité d'une API système ne doit jamais empêcher l'Island de
        // s'afficher.
        await _featureRegistry.StartEnabledAsync();

        MiniLogger.Log($"Fonctionnalités actives : {_featureRegistry.RunningCount}/{_featureRegistry.Features.Count}");

        RearmExpirationTimer();

        MiniLogger.Log("IslandWindow prête");
    }

    // ------------------------------------------------------------------
    // Rétablissement du fil d'interface
    // ------------------------------------------------------------------

    /// <summary>
    /// Exécute une action sur le fil d'interface, immédiatement si on y est déjà.
    ///
    /// Le passage immédiat n'est pas une optimisation : il préserve l'ordre des
    /// opérations sur tous les chemins internes, qui s'exécutent déjà sur le fil
    /// d'interface. Seuls les appels venus d'ailleurs sont confiés au répartiteur.
    /// </summary>
    private void OnUiThread(Action action)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() => action());
    }

    /// <summary>
    /// Demande un rendu, en regroupant les demandes d'une même rafale.
    ///
    /// Une rafle de republications produit plusieurs signaux alors que le rendu
    /// projette l'état <em>courant</em> : le rejouer n'apporterait rien. Le drapeau
    /// évite donc d'empiler des rendus identiques.
    /// </summary>
    private void RequestRender()
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            Render();
            return;
        }

        if (Interlocked.Exchange(ref _renderPending, 1) == 1)
        {
            return;
        }

        bool queued = _dispatcherQueue.TryEnqueue(() =>
        {
            Interlocked.Exchange(ref _renderPending, 0);
            Render();
        });

        if (!queued)
        {
            // Répartiteur fermé : l'application se termine. Rien à rattraper.
            Interlocked.Exchange(ref _renderPending, 0);
        }
    }

    // ------------------------------------------------------------------
    // Rendu
    // ------------------------------------------------------------------

    /// <summary>
    /// Projette l'activité présentée dans la vue correspondant à sa clé de scène.
    /// </summary>
    private void Render()
    {
        // L'achèvement d'un dépôt occupe la notch le temps de sa convergence :
        // le rendu reprend à sa fin, et rattrape alors tout ce qui a changé.
        if (_dropCompleting)
        {
            return;
        }

        IslandActivity? activity = _controller.PresentedActivity;
        bool expanded = _controller.State is IslandState.Expanded or IslandState.Expanding;

        // Le palier au repos ne dépend jamais de l'ouverture : il est résolu à
        // chaque rendu et mémorisé, parce que la fermeture doit retrouver
        // exactement la forme quittée et que le survol doit savoir quoi annoncer.
        _tier = IslandPresentation.Resolve(activity);
        _controller.UpdateCollapsedFootprint(IslandFootprint.For(_tier, _settings.Density));

        foreach (FrameworkElement root in _sceneRoots)
        {
            root.Visibility = Visibility.Collapsed;
        }

        IdleRestView.Visibility = Visibility.Collapsed;
        SignalRestView.Visibility = Visibility.Collapsed;
        CardRestView.Visibility = Visibility.Collapsed;

        UpdateStackIndicator();
        ApplyActivityTint(activity);
        ApplyStateTint(activity);
        UpdateSatellite();

        if (activity is null)
        {
            StopRestingHypnotic();
            IdleRestView.Visibility = Visibility.Visible;

            // L'heure est un réglage et non un défaut : elle installerait une
            // horloge à la minute dans un produit dont la promesse est de ne rien
            // faire au repos. Sans elle, la lèvre est vide — le point de veille
            // ne l'accompagne que pour lui donner un repère.
            IdleClockText.Visibility = _settings.ShowClockAtRest
                ? Visibility.Visible
                : Visibility.Collapsed;

            IdleStatusDot.Visibility = IdleClockText.Visibility;

            IdleClockText.Text = DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture);
            return;
        }

        bool known = _scenes.TryGetValue(activity.SceneKey, out IIslandSceneView? scene);

        if (expanded && known && scene is not null)
        {
            StopRestingHypnotic();
            scene.Apply(activity);
            scene.Root.Visibility = Visibility.Visible;
            return;
        }

        PresentResting(activity);

        // Le signalement ne concerne que la clé inconnue. Une Island fermée n'est
        // pas une anomalie : confondre les deux cas ferait douter d'une clé
        // parfaitement valide, et noierait le vrai signal dans le bruit.
        if (!known)
        {
            ReportUnknownSceneKey(activity);
        }
    }

    /// <summary>
    /// Projette l'activité dans la forme fermée correspondant à son palier.
    ///
    /// La forme au repos ne dépend jamais de la scène déclarée : une lecture
    /// musicale et un compte à rebours ont des scènes très différentes et le même
    /// palier, donc la même forme. C'est ce qui permet d'ajouter une fonctionnalité
    /// sans ajouter une silhouette.
    /// </summary>
    private void PresentResting(IslandActivity activity)
    {
        // L'aperçu d'un signal porte la seconde ligne : c'est l'information qui
        // donne envie de cliquer. Il la porte dans une forme à peine plus grande.
        bool previewing = _controller.State == IslandState.Preview;
        IslandPresentationTier shown = previewing
            ? NotchPresentationResolver.PreviewTier(_tier)
            : _tier;

        HypnoticPreset preset = HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);
        Color tint = HypnoticTint(activity);

        if (shown == IslandPresentationTier.Signal)
        {
            SignalGlyph.Glyph = GlyphCatalog.Resolve(activity.IconKey);
            SignalLabel.Text = activity.Title;
            SignalRestView.Visibility = Visibility.Visible;

            _cardHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
            ApplyHypnoticSlot(_signalHypnotic, SignalHypnoticHost, SignalGlyph, preset, tint);
            return;
        }

        // La densité déplace l'air, jamais la taille du texte : une carte
        // compacte est la même carte avec moins de respiration, et non une carte
        // plus petite. La marge est donc posée ici, à partir de la même table que
        // la hauteur — les deux ne peuvent pas diverger. Pendant l'aperçu d'un
        // signal, elle se déduit de la hauteur de l'aperçu, qui porte les mêmes
        // deux lignes dans moins d'air.
        double padding = shown != _tier
            ? Math.Max(0, (IslandFootprint.PreviewOf(_tier, _settings.Density).Height - CardContentHeight) / 2)
            : IslandFootprint.CardVerticalPadding(_settings.Density);

        CardRestView.Margin = new Thickness(14, padding, 14, padding);

        CardGlyph.Glyph = GlyphCatalog.Resolve(activity.IconKey);

        // Le contexte d'abord, l'état ensuite : une activité qui déclare une
        // ligne de contexte — « Read app-sidebar.tsx · 219 lines » — la voit à
        // la place de la légende calculée.
        CardSubhead.Text = string.IsNullOrWhiteSpace(activity.Eyebrow)
            ? BuildSubhead(activity)
            : activity.Eyebrow;

        CardHeadline.Text = activity.Title;
        CardRestView.Visibility = Visibility.Visible;

        _signalHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        ApplyHypnoticSlot(_cardHypnotic, CardHypnoticHost, CardGlyph, preset, tint);
    }

    /// <summary>
    /// Hauteur des deux lignes d'une carte : légende 14, écart 2, titre 16. Voir
    /// <see cref="IslandFootprint.CardVerticalPadding"/>.
    /// </summary>
    private const double CardContentHeight = 14 + 2 + 16;

    /// <summary>
    /// Met le satellite à jour : il montre l'activité <em>suivante</em>.
    ///
    /// Résolu ici et non à chaque image : parcourir la pile d'activités est une
    /// opération de tri, et la refaire à chaque centaine d'images d'un morphing
    /// serait exactement le travail inutile que le projet s'interdit. Le
    /// placement, lui, suit la géométrie, et se fait donc là où elle est connue.
    /// </summary>
    private void UpdateSatellite()
    {

        bool expanded = _controller.State is IslandState.Expanded or IslandState.Expanding;

        // Ouverte, l'Island montre déjà sa pile par sa propre surface : un satellite
        // en plus serait un second indicateur pour la même information.
        IslandActivity? next = null;

        if (_settings.ShowSatellite && !expanded)
        {
            IReadOnlyList<IslandActivity> stack = _activityManager.GetActiveActivities();

            if (stack.Count > 1)
            {
                next = stack[1];
            }
        }

        if (next is null)
        {
            _satellite?.Apply(null);
            return;
        }

        _satellite ??= CreateSatellite();

        _satellite.UseSpringAnimations = UseSpringAnimations();

        // Placer avant d'afficher : sinon la fenêtre apparaîtrait une image à sa
        // position précédente, ce qui se voit immédiatement lorsque le moniteur ou
        // la densité viennent de changer.
        _satellite.PlaceAround(_lastWindowX, _lastWindowY, _lastWindowWidth, _lastWindowHeight);
        _satellite.Apply(next);
    }

    private SatelliteWindow CreateSatellite()
    {
        var satellite = new SatelliteWindow();
        satellite.Clicked += OnSatelliteClicked;

        // La couche décorative reste sous les surfaces interactives : l'ombre et la
        // dissolution ne doivent jamais passer devant un objet cliquable.
        _atmosphere.PlaceBehind(_hWnd);

        MiniLogger.Log("Satellite détaché créé");

        return satellite;
    }

    /// <summary>
    /// Un clic sur le satellite présente l'activité suivante.
    ///
    /// Le chemin est exactement celui de la molette et des flèches : parcourir la
    /// pile. Le satellite annonce qu'il y a une suite, et le clic va la chercher —
    /// il n'invente pas un second arbitrage qui pourrait contredire le premier.
    /// </summary>
    private void OnSatelliteClicked(object? sender, EventArgs e)
    {
        if (_controller.CyclePresentation(1))
        {
            _diagnostics.CountEvent();
        }
    }

    /// <summary>
    /// Première ligne de la carte : le détail, puis l'état en mots.
    ///
    /// L'état est nommé et pas seulement coloré, parce qu'une information portée
    /// par la seule couleur disparaît en contraste élevé, échappe à une partie des
    /// daltonismes, et se perd pour quiconque ne distingue pas deux teintes
    /// voisines. Les états qui n'ont rien à dire — la veille, un retour système —
    /// ne sont pas nommés : les écrire ajouterait un mot sans information. Voir
    /// ADR-014.
    /// </summary>
    private static string BuildSubhead(IslandActivity activity)
    {
        string detail = activity.Subtitle ?? activity.Source ?? string.Empty;
        string state = StatePalette.Label(activity.State);

        if (detail.Length == 0)
        {
            return state.Length == 0 ? "SpaceNotch" : state;
        }

        return state.Length == 0 ? detail : $"{detail} · {state}";
    }

    /// <summary>
    /// Signale une clé de scène inconnue, une seule fois par clé.
    ///
    /// Sans ce signal, une fonctionnalité qui déclare une clé inexistante — cas
    /// fréquent pour un greffon tiers — se contenterait de ne jamais s'afficher,
    /// sans rien dire. Le repli reste silencieux dans l'Island, comme il se doit,
    /// mais il laisse une trace exploitable pour qui développe la fonctionnalité.
    /// La mémoire des clés déjà signalées évite de remplir le journal à chaque
    /// republication d'activité.
    /// </summary>
    private void ReportUnknownSceneKey(IslandActivity activity)
    {
        if (_reportedUnknownScenes.Add(activity.SceneKey))
        {
            MiniLogger.Log(
                $"[SCENE] clé inconnue « {activity.SceneKey} » "
                + $"(fonctionnalité {activity.FeatureId}) : repli sur la pilule. "
                + $"Clés valides : {string.Join(", ", IslandSceneCatalog.AllKeys)}");
        }
    }

    /// <summary>
    /// Affiche le nombre d'activités en attente derrière celle qui est présentée.
    ///
    /// L'indicateur n'apparaît qu'à partir de deux activités : afficher « 1 » en
    /// permanence ajouterait du bruit sans rien apprendre.
    /// </summary>
    private void UpdateStackIndicator()
    {
        int count = _activityManager.Count;

        // Le satellite est la façon normale de signaler une seconde activité ;
        // le compteur n'est que son repli, lorsque l'utilisateur l'a éteint. Les
        // deux ne s'affichent jamais ensemble : deux indicateurs pour une même
        // information feraient douter qu'ils disent la même chose.
        bool visible = _settings.ShowActivityStack && !_settings.ShowSatellite && count > 1;

        // Des points plutôt qu'un nombre : la pile se constate, elle ne se lit
        // pas. Au-delà de quatre, un point de plus n'apprendrait rien.
        string text = visible ? string.Join(" ", Enumerable.Repeat("•", Math.Min(count, 4))) : string.Empty;
        Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;

        SignalStackIndicator.Text = text;
        SignalStackIndicator.Visibility = state;
        CardStackIndicator.Text = text;
        CardStackIndicator.Visibility = state;
    }

    /// <summary>
    /// Projette la teinte déclarée par l'activité présentée sur l'atmosphère.
    ///
    /// La fenêtre ne devine rien : une activité qui ne déclare pas de teinte
    /// laisse l'atmosphère à sa valeur de référence, et une fonctionnalité qui en
    /// déclare une — la lecture média, par exemple — colore le halo.
    /// </summary>
    private void ApplyActivityTint(IslandActivity? activity)
    {
        // Sans teinte déclarée, le halo prend celle de l'état plutôt qu'une
        // couleur propre : un halo neutre qui accompagne un glyphe ambré se lit
        // comme une lumière d'une autre source, et le regard cherche une
        // signification qui n'existe pas.
        Color state = StatePalette.Tint(activity?.State ?? IslandActivityState.Idle);

        AmbientState ambient = AmbientState.For(
            activity,
            new ActivityTint(state.R, state.G, state.B),
            _visualState.HighContrast);

        _atmosphere.SetGlowIntensity(ambient.Intensity, ambient.TintOpacity);
        _atmosphere.SetGlowColor(Color.FromArgb(0xFF, ambient.Tint.R, ambient.Tint.G, ambient.Tint.B));

        // La dissolution respire avec la matière qui travaille : même fonction,
        // même période. Elle n'est relancée qu'au changement de mouvement, sans
        // quoi chaque rendu la ferait repartir de son image de départ.
        HypnoticPreset preset = activity is null || !AnimateHypnotic() || ambient.Pulse <= 0
            ? HypnoticPreset.None
            : HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);

        if (preset != _atmospherePreset)
        {
            _atmospherePreset = preset;
            _atmosphere.SetHypnoticPulse(preset, ambient.Pulse);
        }
    }

    /// <summary>
    /// Applique la teinte d'état au glyphe de tête et au point de veille.
    ///
    /// C'est la même teinte qui colore le halo — la lumière et le glyphe viennent
    /// donc bien de la même source. En contraste élevé, la teinte cède la place à
    /// l'encre du système : une couleur d'ambiance y dégraderait la lisibilité
    /// sans rien apprendre. Voir ADR-014.
    /// </summary>
    private void ApplyStateTint(IslandActivity? activity)
    {
        IslandActivityState state = activity?.State ?? IslandActivityState.Idle;

        Brush tint = StatePalette.Brush(_visualState.HighContrast ? IslandActivityState.Idle : state);

        IdleStatusDot.Fill = tint;
        SignalGlyph.Foreground = tint;
        CardGlyph.Foreground = tint;
    }

    /// <summary>
    /// Applique la silhouette d'un encombrement.
    ///
    /// Le rayon ne dépend pas de la taille : c'est ce qui fait lire l'ouverture
    /// comme un seul objet qui change de taille plutôt que comme deux objets
    /// différents. Un rayon de 12 sur une forme de 28 de haut donne une pastille,
    /// le même rayon sur une forme de 52 donne un panneau — et c'est bien le même
    /// objet. Voir ADR-012.
    ///
    /// Les coins supérieurs restent droits : l'Island est ancrée au bord de
    /// l'écran, un arrondi en haut la détacherait de la surface sur laquelle elle
    /// est censée reposer.
    /// </summary>
    private void ApplyShape(IslandFootprint footprint)
    {
        NotchGeometry geometry = _settings.Geometry;

        double radius = geometry.RadiusFor(footprint);
        double shoulder = geometry.ShoulderFor(footprint);

        Geometry? silhouette = _shape.Build(footprint, radius, geometry.Smoothing, shoulder: shoulder);

        // Une géométrie nulle signifie « identique à la précédente » : le tracé
        // déjà posé est conservé, ce qui évite une reconstruction par image
        // quand le ressort ne bouge plus.
        if (silhouette is not null)
        {
            SurfaceFill.Data = silhouette;
        }

        // Le reflet suit la même courbe, borné à sa bande. La borne est ce qui
        // l'empêche de mordre dans les congés sur les paliers bas : à 34 de haut,
        // une bande de 22 lui ferait dessiner sa propre arête.
        double band = Math.Min(_specularHeight, footprint.Height * 0.45);
        Geometry? specular = _shape.Build(footprint, radius, geometry.Smoothing, band, shoulder);

        if (specular is not null)
        {
            SpecularFill.Data = specular;
        }

        // Le contenu se mesure depuis les flancs, pas depuis les épaules : les
        // épaules appartiennent au bord de l'écran, aucun texte n'y a sa place.
        if (Math.Abs(shoulder - _contentShoulder) > 0.25)
        {
            _contentShoulder = shoulder;
            ContentArea.Margin = new Thickness(shoulder, 0, shoulder, 0);
        }
    }

    /// <summary>
    /// Encombrement annoncé par un survol : la même forme, descendue et élargie
    /// de 10 à 20 %. Le survol invite, c'est le clic qui ouvre. Voir
    /// <see cref="IslandFootprint.PreviewOf"/>.
    /// </summary>
    private IslandFootprint ResolvePreviewFootprint()
        => IslandFootprint.PreviewOf(_tier, _settings.Density);

    /// <summary>
    /// Hauteur de la bande de reflet, lue dans les jetons. Un jeton absent ne doit
    /// pas faire disparaître le reflet — une panne de thème doit dégrader la
    /// lumière, pas éteindre la surface.
    /// </summary>
    private static double ResolveSpecularHeight()
        => Application.Current?.Resources?.TryGetValue("NfSpecularHeight", out object? value) == true
            && value is double height
                ? height
                : 22;

    // ------------------------------------------------------------------
    // Géométrie
    // ------------------------------------------------------------------

    /// <summary>
    /// Applique un encombrement exprimé en DIPs.
    ///
    /// La conversion se fait avec l'échelle du moniteur <em>cible</em> : deux
    /// écrans peuvent avoir des échelles différentes dans le même bureau
    /// virtuel, et utiliser l'échelle de la fenêtre placerait l'Island de
    /// travers sur un poste 4K à côté d'un écran 100 %.
    /// </summary>
    private void ApplyGeometry(IslandFootprint footprint)
    {

        DisplayInfo display = ResolveDisplay();
        double scale = display.DpiScale;

        int widthPx = MonitorDpi.ToPhysicalPixels(footprint.Width, scale);
        int heightPx = MonitorDpi.ToPhysicalPixels(footprint.Height, scale);
        int offsetX = MonitorDpi.ToPhysicalPixels(_settings.HorizontalOffset, scale);

        int x = display.Left + ((display.Width - widthPx) / 2) + offsetX;

        // Règle n°1 : le bord supérieur de la notch est le bord supérieur du
        // moniteur, sans aucun décalage. Il n'existe pas de réglage qui la
        // décolle — une notch décalée vers le bas est une capsule flottante.
        int y = display.Top;

        // Beaucoup d'images du ressort retombent sur le même rectangle en
        // pixels entiers, en particulier en fin de course où les écarts passent
        // sous le pixel. Resoumettre alors la même géométrie enchaîne un
        // SetWindowPos et une reprise de composition DWM pour rien. La mesure
        // reste exacte : seule la redondance disparaît.
        if (x != _lastWindowX
            || y != _lastWindowY
            || widthPx != _lastWindowWidth
            || heightPx != _lastWindowHeight)
        {
            _lastWindowX = x;
            _lastWindowY = y;
            _lastWindowWidth = widthPx;
            _lastWindowHeight = heightPx;

            _appWindow.MoveAndResize(new RectInt32(x, y, widthPx, heightPx));

            // L'Island vient de changer de place : ce qu'elle recouvre a changé
            // avec elle. La relecture n'a lieu qu'ici, c'est-à-dire quand le
            // rectangle a réellement bougé, et jamais à chaque image.
            _presence.Recheck();
        }

        // Le corps XAML, lui, suit chaque image : c'est lui qui porte le morphing
        // sous-pixel, et il ne coûte qu'une passe de disposition sur un arbre
        // minuscule — sans aucun aller-retour avec le gestionnaire de fenêtres.
        IslandBody.Width = footprint.Width;
        IslandBody.Height = footprint.Height;

        ApplyShape(footprint);

        // Le satellite suit la géométrie de l'Island, sans repasser par le
        // parcours de la pile : seule sa position dépend de ce calcul.
        if (_satellite is { IsShown: true })
        {
            _satellite.PlaceAround(x, y, widthPx, heightPx);
        }

        // Défensif : la géométrie est aussi calculée pendant la construction de
        // la fenêtre, avant que la couche décorative existe.
        NotchGeometry geometry = _settings.Geometry;

        _atmosphere?.PositionAround(new AtmospherePlacement(
            x,
            y,
            widthPx,
            heightPx,
            footprint.Width,
            footprint.Height,
            geometry.RadiusFor(footprint),
            geometry.ShoulderFor(footprint),
            DeploymentFor(footprint.Height)));
    }

    /// <summary>
    /// Avancement du déploiement, déduit de la hauteur de la forme.
    ///
    /// La dissolution naît de la croissance, elle n'est pas déclenchée par un
    /// état. À la hauteur d'une carte elle est absente, et elle atteint son plein
    /// régime sur une scène ouverte. Un seuil piloté par la machine d'état se
    /// déclencherait à un instant précis du morphing, ce qui se lirait comme un
    /// clignotement — alors que l'avancement continu, lui, ne peut pas se
    /// produire au mauvais moment.
    /// </summary>
    private static double DeploymentFor(double heightDip)
        => Math.Clamp((heightDip - DissolveOnsetDip) / DissolveSpanDip, 0.0, 1.0);

    /// <summary>Hauteur à laquelle la dissolution commence à apparaître, en DIPs.</summary>
    private const double DissolveOnsetDip = 64;

    /// <summary>Hauteur sur laquelle elle atteint son plein régime, en DIPs.</summary>
    private const double DissolveSpanDip = 80;

    private DisplayInfo ResolveDisplay()
    {
        switch (_settings.DisplayMode)
        {
            case IslandDisplayMode.Current:
                return DisplayManager.ResolveFromCursor();

            case IslandDisplayMode.Custom when _settings.CustomDisplayHandle is > 0:
                return DisplayManager.FromMonitor(new IntPtr(_settings.CustomDisplayHandle.Value));

            default:
                return DisplayManager.GetPrimaryDisplay();
        }
    }

    // ------------------------------------------------------------------
    // Réaction à l'environnement (sans scrutation)
    // ------------------------------------------------------------------

    private void OnWindowMessageReceived(object? sender, WindowMessageEventArgs e)
    {
        // Les notifications système diffusées par message sont routées vers les
        // fonctionnalités concernées — le presse-papier en est l'exemple. Aucune
        // scrutation n'est nécessaire pour les recevoir.
        _featureRegistry.TryHandleWindowMessage(e.Message.MessageId, e.Message.WParam);

        if (_screenWatcher.HandleMessage(e.Message.MessageId, e.Message.WParam))
        {
            ScheduleEnvironmentRefresh();
        }

        // Les bascules plein écran qui ne changent pas de fenêtre au premier plan
        // — certaines vidéos — n'émettent aucun des événements surveilllés. Elles
        // changent en revanche l'environnement du bureau, donc la relecture est
        // faite ici : c'est ce qui les rattrape.
        _presence.Recheck();
    }

    /// <summary>
    /// Suit la présence de la session : l'Island se retire quand une application
    /// occupe l'écran, et reparaît exactement dans la forme qu'elle occupait.
    /// </summary>
    private void ApplyPresence()
    {
        bool hide = _settings.HideOverFullscreen && _presence.ShouldHide;

        SetIslandVisible(!hide);
    }

    /// <summary>
    /// Montre ou retire l'Island, sans toucher à son état.
    ///
    /// L'état — activité présentée, palier, position du ressort — n'est pas
    /// modifié : revenir doit rendre l'objet tel qu'il a été quitté, sinon un
    /// changement de fenêtre en plein milieu d'une session replierait l'Island à
    /// son gré.
    /// </summary>
    private void SetIslandVisible(bool visible)
    {
        if (_isClosed)
        {
            return;
        }

        if (visible)
        {
            // La géométrie et l'ordre de superposition sont repris avant
            // l'affichage : sinon la fenêtre apparaîtrait une image à sa position
            // d'avant, ce qui se voit immédiatement après un changement d'écran.
            ApplyGeometry(_controller.CurrentFootprint);
            _atmosphere.SetVisible(true);
            _atmosphere.PlaceBehind(_hWnd);
            _appWindow.Show();
            return;
        }

        _satellite?.Apply(null);
        _atmosphere.SetVisible(false);
        _appWindow.Hide();
    }

    private void OnEnvironmentChanged(object? sender, ScreenChangeKind kind)
    {
        _diagnostics.CountEvent();
        ScheduleEnvironmentRefresh();
    }

    /// <summary>
    /// Les messages de changement arrivent en rafale (un changement de résolution
    /// en produit plusieurs). Un unique minuteur à usage unique les coalesce en
    /// une seule réévaluation, puis se désarme.
    /// </summary>
    private void ScheduleEnvironmentRefresh()
    {
        _geometryTimer ??= CreateOneShotTimer(
            TimeSpan.FromMilliseconds(120),
            RefreshFromEnvironment);

        _geometryTimer.Stop();
        _geometryTimer.Start();
    }

    private void RefreshFromEnvironment()
    {
        SystemVisualState updated = SystemVisualState.Read();

        if (updated != _visualState)
        {
            _visualState = updated;
            ApplyBackdropMode();

            // L'atmosphère doit cesser d'animer si Windows demande la réduction
            // des animations : la teinte suit alors instantanément.
            _atmosphere.UseSpringAnimations = UseSpringAnimations();

            _eventBus.Publish(new SystemVisualStateChangedEvent(
                updated.AnimationsEnabled,
                updated.TransparencyEffectsEnabled,
                updated.HighContrast));
        }

        ApplyGeometry(_controller.CurrentFootprint);
        _atmosphere.PlaceBehind(_hWnd);

        MiniLogger.Log($"Environnement rafraîchi : {updated}");
    }

    // ------------------------------------------------------------------
    // Expiration des activités
    // ------------------------------------------------------------------

    /// <summary>
    /// Arme un unique minuteur pour la prochaine échéance, et le désarme s'il n'y
    /// a plus rien à faire expirer. Aucune vérification périodique n'est donc
    /// effectuée en l'absence d'activité temporaire.
    /// </summary>
    private void RearmExpirationTimer()
    {
        TimeSpan? delay = _activityManager.GetTimeUntilNextExpiration(DateTimeOffset.UtcNow);

        if (delay is null)
        {
            _expirationTimer?.Stop();
            return;
        }

        _expirationTimer ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(100), OnExpirationTick);

        // Plancher de 50 ms pour ne pas armer un minuteur déjà échu.
        _expirationTimer.Interval = delay.Value < TimeSpan.FromMilliseconds(50)
            ? TimeSpan.FromMilliseconds(50)
            : delay.Value;

        _expirationTimer.Stop();
        _expirationTimer.Start();
    }

    private void OnExpirationTick()
    {
        int expired = _activityManager.ExpireOverdue(DateTimeOffset.UtcNow);

        if (expired > 0)
        {
            MiniLogger.Log($"Expiration : {expired} activité(s) retirée(s)");
        }

        RearmExpirationTimer();
    }

    private DispatcherQueueTimer CreateOneShotTimer(TimeSpan interval, Action onTick)
    {
        DispatcherQueueTimer timer = _dispatcherQueue.CreateTimer();
        timer.IsRepeating = false;
        timer.Interval = interval;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            onTick();
        };

        return timer;
    }

    // ------------------------------------------------------------------
    // Mode de composition
    // ------------------------------------------------------------------

    private IslandBackdropMode ResolveBackdropMode()
    {
        if (_settings.BackdropMode != IslandBackdropMode.Auto)
        {
            return _settings.BackdropMode;
        }

        // En mode automatique, on choisit le rendu de référence si le système
        // autorise encore la transparence, sinon le repli déterministe.
        return _visualState.TransparencyEffectsEnabled
            ? IslandBackdropMode.Transparent
            : IslandBackdropMode.Opaque;
    }

    /// <summary>
    /// Applique le mode de fond aux deux surfaces. Le mode transparent laisse le
    /// bureau traverser le fondu du bas ; le mode opaque garantit un rendu
    /// identique quel que soit le réglage système ; le mode flouté utilise le
    /// pinceau de fond du compositeur.
    /// </summary>
    private void ApplyBackdropMode()
    {
        IslandBackdropMode mode = ResolveBackdropMode();

        // Une affectation par branche, avec un type cible explicite : on évite
        // ainsi toute inférence de type commun entre backdrops d'origines
        // différentes.
        switch (mode)
        {
            case IslandBackdropMode.Opaque:
                // Aucun backdrop : la surface porte elle-même son fond opaque.
                SystemBackdrop = null;
                break;

            case IslandBackdropMode.Blurred:
                SystemBackdrop = new BlurredBackdrop();
                break;

            default:
                SystemBackdrop = TransparentBackdrop;
                break;
        }

        // La couche décorative reste transparente dans tous les cas : elle ne
        // porte aucune surface, seulement de la lumière. Son fond est donc
        // déclaré une fois pour toutes dans son propre XAML.
        // La voie du compositeur est la référence ; le réglage permet de revenir au
        // dégradé XAML sur la même session, ce qui rend les deux comparables.
        _atmosphere.SetCompositionEnabled(_settings.UseCompositionAtmosphere);

        // Sonde : la dissolution est-elle réellement calculée par le compositeur,
        // ou retombons-nous sur le dégradé XAML ? La question est posée à la
        // surface, qui seule connaît le résultat de sa tentative d'attachement.
        // Sans cette remontée, un repli silencieux serait indiscernable du rendu
        // de référence.
        _diagnostics.ReportAtmospherePath(_atmosphere.UsesCompositionSurface
            ? AtmosphereRenderPath.Composition
            : AtmosphereRenderPath.XamlFallback);

        // L'ombre est sondée séparément : elle ne dépend pas de la même capacité
        // que la dissolution, et une ombre refusée doit se voir dans le rapport
        // plutôt que de se confondre avec une ombre invisible sur fond sombre.
        _diagnostics.ReportShadow(_atmosphere.UsesCompositionShadow);

        bool light = _settings.Appearance == IslandAppearance.Light;

        // La teinte va au tracé, pas au panneau : la surface n'est plus un
        // rectangle arrondi mais une silhouette, et seule une forme sait la
        // remplir sans déborder de ses congés.
        SurfaceFill.Fill = AtmosphericMaskHelper.CreateSolidSurface(
            light: light,
            opaque: mode == IslandBackdropMode.Opaque);

        // Les contrôles qui ne viennent pas de nos jetons — tout ce que Fluent
        // dessine, à commencer par les boutons d'une scène — suivent le thème
        // demandé **au niveau de la fenêtre**.
        //
        // Sans cela, ils suivent le thème de Windows. Sur un système en thème
        // clair, une carte noire se retrouvait donc remplie de contrôles clairs :
        // le bouton principal apparaissait en pastille blanche à texte noir, ce
        // qui ne se rattrape ni par une couleur ni par une marge — c'est un
        // désaccord de thème, pas un problème de palette.
        RootLayout.RequestedTheme = light ? ElementTheme.Light : ElementTheme.Dark;

        // L'encre, elle, n'est plus posée ici : les styles de texte résolvent
        // leur pinceau depuis les jetons de thème, qui connaissent déjà le thème
        // clair. La poser aussi ici aurait fait deux sources pour une même
        // couleur, et la dernière écrite aurait gagné au hasard.

        MiniLogger.Log(
            $"Apparence appliquée : {mode}, thème {(light ? "clair" : "sombre")}, "
            + $"dissolution {RuntimeDiagnostics.Describe(_diagnostics.AtmospherePath)}");
    }

    // ------------------------------------------------------------------
    // Interactions
    // ------------------------------------------------------------------

    private void OnIslandPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        // Une entrée annule la fermeture en attente : le pointeur qui revient
        // dans le délai de grâce retrouve l'aperçu au lieu de le faire renaître.
        _previewExitTimer?.Stop();

        if (_settings.HoverToPreview)
        {
            // Intention de survol : le pointeur qui ne fait que longer le bord de
            // l'écran — pour atteindre un onglet, un menu — ne doit pas faire
            // bouger la notch. Un temps de pose court suffit à distinguer les
            // deux ; un aperçu déjà ouvert, lui, est repris sans attendre.
            if (_controller.State == IslandState.Preview)
            {
                _controller.RequestPreview();
            }
            else
            {
                _previewEnterTimer ??= CreateOneShotTimer(PreviewEnterDwell, _controller.RequestPreview);
                _previewEnterTimer.Stop();
                _previewEnterTimer.Start();
            }
        }

        // Le clavier n'est capté que tant que l'utilisateur désigne l'Island : le
        // reste du temps, la fenêtre ne peut pas être activée et ne perturbère en
        // rien la frappe dans l'application au premier plan.
        // Aucun journal ici : le pointeur entre et sort en rafale lorsque la
        // géométrie change, et journaliser chaque passage noierait le journal sous
        // un bruit sans information.
        WindowChrome.SetKeyboardCapture(_hWnd, enabled: true);
        IslandBody.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Sortie du pointeur : l'aperçu se retire après un délai de grâce.
    ///
    /// Le délai n'est pas un confort : la géométrie change sous le pointeur
    /// pendant que la forme grandit, si bien que le pointeur « sort » et « entre »
    /// plusieurs fois au cours d'une même animation. Fermer immédiatement
    /// produirait un clignotement, et l'utilisateur verrait l'Island hésiter sous
    /// sa souris.
    /// </summary>
    private void OnIslandPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // Un passage trop bref n'a jamais été une intention : l'aperçu n'a pas
        // lieu du tout.
        _previewEnterTimer?.Stop();

        _previewExitTimer ??= CreateOneShotTimer(PreviewExitGrace, OnPreviewExitTick);

        _previewExitTimer.Stop();
        _previewExitTimer.Start();

        WindowChrome.SetKeyboardCapture(_hWnd, enabled: false);
    }

    private void OnPreviewExitTick() => _controller.EndPreview();

    /// <summary>
    /// Temps de pose avant l'aperçu. Les recommandations d'usage placent
    /// l'intention entre 0,3 et 0,5 s pour un contenu qui s'ouvre ; l'aperçu
    /// n'ouvre rien, il fait seulement descendre la notch de quelques DIPs, d'où
    /// une pose plus courte. Le clic, lui, n'attend jamais.
    /// </summary>
    private static readonly TimeSpan PreviewEnterDwell = TimeSpan.FromMilliseconds(220);

    /// <summary>Délai de grâce avant que l'aperçu ne se retire.</summary>
    private static readonly TimeSpan PreviewExitGrace = TimeSpan.FromMilliseconds(1500);

    /// <summary>
    /// Clic sur l'Island.
    ///
    /// Deux gestes, deux intentions : le clic gauche déplie ce qui est présenté ou
    /// le replie, et le clic droit ouvre la grille de fonctions. Sans le second,
    /// une fonctionnalité activée dans les réglages resterait hors d'atteinte tant
    /// qu'aucune activité ne se manifeste d'elle-même — c'est-à-dire presque
    /// toujours. Au repos sans aucune activité, le clic gauche ouvre la même
    /// grille : il n'y a rien d'autre à déplier.
    /// </summary>
    private void OnIslandPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        // Le clic exprime l'intention : l'aperçu en attente n'a plus lieu d'être.
        _previewEnterTimer?.Stop();

        if (e.GetCurrentPoint(IslandBody).Properties.IsRightButtonPressed
            || (_controller.State is IslandState.Closed && _controller.PresentedActivity is null))
        {
            e.Handled = true;
            _launcherFeature.Show();
            RevealPresented();
            return;
        }

        _controller.ToggleFromUser();
    }

    /// <summary>
    /// Ouvre l'Island sur ce qu'une fonctionnalité vient de présenter.
    ///
    /// Un geste de l'utilisateur — un clic, une entrée de menu — est une demande
    /// de **voir**. Publier l'activité ne suffit donc pas : une activité de
    /// priorité normale ne déplie pas l'Island, si bien qu'un minuteur lancé
    /// depuis le menu tournait sans aucun contrôle atteignable, et qu'une grille
    /// de fonctions ne s'affichait nulle part. C'est l'intention de l'utilisateur
    /// qui ouvre, ici, et non la prétendue urgence d'une activité.
    /// </summary>
    private void RevealPresented()
    {
        if (_controller.PresentedActivity is null)
        {
            // Une fonctionnalité désactivée ne publie rien : ouvrir une forme vide
            // serait pire que ne rien faire.
            return;
        }

        _controller.RequestExpand();
    }

    /// <summary>
    /// Molette sur l'Island : parcourt la pile d'activités. C'est l'interaction
    /// qui donne accès à ce qui est en attente derrière l'activité présentée.
    /// </summary>
    private void OnIslandPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        int delta = e.GetCurrentPoint(IslandBody).Properties.MouseWheelDelta;

        if (delta == 0)
        {
            return;
        }

        if (_controller.CyclePresentation(delta > 0 ? 1 : -1))
        {
            _diagnostics.CountEvent();
            e.Handled = true;
        }
    }

    /// <summary>
    /// Clavier de l'Island : Échap réduit, les flèches parcourent la pile,
    /// Entrée exécute l'action principale de l'activité présentée.
    ///
    /// Le rendu ne décide pas ce que fait l'action : il demande l'exécution de
    /// celle que la fonctionnalité a désignée comme principale.
    /// </summary>
    private async void OnIslandKeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case global::Windows.System.VirtualKey.Escape:
                _controller.RequestCollapse();
                break;

            case global::Windows.System.VirtualKey.Left:
            case global::Windows.System.VirtualKey.Up:
                _controller.CyclePresentation(-1);
                break;

            case global::Windows.System.VirtualKey.Right:
            case global::Windows.System.VirtualKey.Down:
                _controller.CyclePresentation(1);
                break;

            case global::Windows.System.VirtualKey.Enter:
            case global::Windows.System.VirtualKey.Space:
                await InvokePrimaryActionAsync();
                break;

            default:
                return;
        }

        _diagnostics.CountEvent();
        e.Handled = true;
    }

    private async Task InvokePrimaryActionAsync()
    {
        IslandActivity? activity = _controller.PresentedActivity;
        ActivityAction? primary = activity?.Actions.FirstOrDefault(a => a.IsPrimary);

        if (activity is null || primary is null)
        {
            return;
        }

        await _featureRegistry.HandleActionAsync(new IslandActionRequest(activity.Id, primary.Id));
    }

    /// <summary>
    /// Un fichier survole la notch : elle devient une cible visuelle, et la
    /// matière « Drop » attire vers son centre.
    /// </summary>
    private void OnIslandDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Déposer dans la notch";

        // DragOver arrive en rafale : la mise en place n'a lieu qu'une fois.
        if (DropZoneView.Visibility == Visibility.Visible)
        {
            return;
        }

        foreach (FrameworkElement root in _sceneRoots)
        {
            root.Visibility = Visibility.Collapsed;
        }

        StopRestingHypnotic();
        IdleRestView.Visibility = Visibility.Collapsed;
        SignalRestView.Visibility = Visibility.Collapsed;
        CardRestView.Visibility = Visibility.Collapsed;
        DropZoneView.Visibility = Visibility.Visible;

        _controller.BeginDragTarget(IslandSceneCatalog.FootprintFor(IslandSceneCatalog.DropZone));
        ShowDropMatter(HypnoticPreset.Drop);
    }

    private void OnIslandDragLeave(object sender, DragEventArgs e)
    {
        if (_dropCompleting)
        {
            return;
        }

        ShowDropMatter(HypnoticPreset.None);
        DropZoneView.Visibility = Visibility.Collapsed;
        _controller.EndDragTarget();
        Render();
    }

    private async void OnIslandDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            return;
        }

        try
        {
            var items = await e.DataView.GetStorageItemsAsync();

            foreach (var item in items)
            {
                if (item is global::Windows.Storage.StorageFile file)
                {
                    _shelfManager.AddFile(file.Path);
                }
            }

            FileShelfSceneView.UpdateItems(_shelfManager.GetItems());

            // Absorption : la matière converge et pulse, puis rend la main à
            // l'étagère. Sans animation, le geste se conclut immédiatement.
            BeginDropCompletion();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[WARN] Dépôt de fichiers interrompu", ex);
            FinishDrop();
        }
    }

    /// <summary>Glyphe fixe ou matière hypnotique, pour la cible de dépôt.</summary>
    private void ShowDropMatter(HypnoticPreset preset)
    {
        bool hypnotic = _dropHypnotic is not null && preset != HypnoticPreset.None;

        DropGlyph.Visibility = hypnotic ? Visibility.Collapsed : Visibility.Visible;
        _dropHypnotic?.SetTint(WarmHypnoticTint());
        _dropHypnotic?.SetPreset(preset, AnimateHypnotic());
    }

    private void BeginDropCompletion()
    {
        if (_dropHypnotic is null || !AnimateHypnotic())
        {
            FinishDrop();
            return;
        }

        _dropCompleting = true;
        ShowDropMatter(HypnoticPreset.Complete);

        // Le compositeur signale la fin du geste ; ce minuteur n'est qu'un filet,
        // pour qu'une fin jamais signalée ne laisse pas la notch figée.
        _dropCompletionTimer ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(1600), FinishDrop);
        _dropCompletionTimer.Stop();
        _dropCompletionTimer.Start();
    }

    /// <summary>Conclut un dépôt : la cible disparaît, la notch reprend sa forme et son contenu.</summary>
    private void FinishDrop()
    {
        _dropCompletionTimer?.Stop();
        _dropCompleting = false;

        ShowDropMatter(HypnoticPreset.None);
        DropZoneView.Visibility = Visibility.Collapsed;
        _controller.EndDragTarget();
        Render();
    }

    // ------------------------------------------------------------------
    // Zone de notification
    // ------------------------------------------------------------------

    private void SetupTrayIcon()
    {
        try
        {
            var windowManager = WindowManager.Get(this);
            windowManager.IsVisibleInTray = true;
            windowManager.TrayIconSelected += (_, _) => _controller.ToggleFromUser();

            windowManager.TrayIconContextMenu += (_, e) => e.Flyout = BuildTrayMenu();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[WARN] Zone de notification indisponible", ex);
        }
    }

    /// <summary>
    /// Menu de la zone de notification, organisé comme les réglages.
    ///
    /// <para>
    /// Le menu suit le vocabulaire du produit plutôt que la liste des
    /// fonctionnalités : ce qu'on <em>lance</em>, ce qui s'<em>affiche</em>,
    /// comment la notch <em>bouge</em>, et à quoi elle <em>ressemble</em>. Les
    /// réglages fins restent dans la fenêtre de réglages ; le menu ne porte que
    /// les gestes qu'on fait sans vouloir ouvrir une fenêtre.
    /// </para>
    ///
    /// <para>
    /// Chaque étiquette dit ce que le clic <em>va faire</em>, pas ce que l'entrée
    /// désigne : un même élément qui démarre et arrête sans le dire laisse croire
    /// qu'il n'y a aucun moyen d'arrêter.
    /// </para>
    /// </summary>
    private MenuFlyout BuildTrayMenu()
    {
        var flyout = new MenuFlyout();

        bool expanded = _controller.State is IslandState.Expanded or IslandState.Expanding;

        var toggleItem = new MenuFlyoutItem
        {
            Text = expanded ? "Réduire la notch" : "Déployer la notch",
            Icon = new FontIcon { Glyph = expanded ? "\uE70E" : "\uE70D" }
        };
        toggleItem.Click += (_, _) => _controller.ToggleFromUser();

        flyout.Items.Add(toggleItem);
        flyout.Items.Add(BuildLaunchMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildActivitiesMenu());
        flyout.Items.Add(BuildMotionMenu());
        flyout.Items.Add(BuildAppearanceMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem
        {
            Text = "Réglages…",
            Icon = new FontIcon { Glyph = "\uE713" }
        };
        settingsItem.Click += (_, _) => OpenSettingsWindow();
        flyout.Items.Add(settingsItem);

        // Les diagnostics sont une information, pas une action : ils ne
        // s'affichent que lorsqu'ils sont activés, et jamais comme un bouton.
        if (_settings.EnableDiagnostics)
        {
            flyout.Items.Add(new MenuFlyoutItem
            {
                Text = _diagnostics.BuildCompactSummary(),
                IsEnabled = false
            });
        }

        flyout.Items.Add(new MenuFlyoutSeparator());

        var exitItem = new MenuFlyoutItem { Text = "Quitter SpaceNotch" };
        exitItem.Click += (_, _) => Application.Current.Exit();
        flyout.Items.Add(exitItem);

        return flyout;
    }

    /// <summary>Ce qu'on lance depuis la notch : applications, minuteurs, focus.</summary>
    private MenuFlyoutSubItem BuildLaunchMenu()
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Lancer",
            Icon = new FontIcon { Glyph = "\uE768" }
        };

        var launcherItem = new MenuFlyoutItem { Text = "Applications…" };
        launcherItem.Click += (_, _) =>
        {
            _launcherFeature.Show();
            RevealPresented();
        };

        bool countdown = _timerFeature.IsMeasuring && _timerFeature.Mode is TimerMode.Countdown;
        var timerItem = new MenuFlyoutItem { Text = countdown ? "Arrêter le minuteur" : "Minuteur (5 min)" };
        timerItem.Click += (_, _) =>
        {
            // Arrêter depuis le menu n'implique pas d'ouvrir la notch : la mesure
            // cesse, il n'y a plus rien à regarder.
            _timerFeature.SetMode(TimerMode.Countdown);
            _timerFeature.Toggle();

            if (!countdown)
            {
                RevealPresented();
            }
        };

        bool stopwatch = _timerFeature.IsMeasuring && _timerFeature.Mode is TimerMode.Stopwatch;
        var stopwatchItem = new MenuFlyoutItem { Text = stopwatch ? "Arrêter le chronomètre" : "Chronomètre" };
        stopwatchItem.Click += (_, _) =>
        {
            _timerFeature.SetMode(TimerMode.Stopwatch);
            _timerFeature.Toggle();

            if (!stopwatch)
            {
                RevealPresented();
            }
        };

        var focusItem = new MenuFlyoutItem
        {
            Text = _pomodoroFeature.IsSessionRunning ? "Mettre le focus en pause" : "Focus (25 min)"
        };
        focusItem.Click += (_, _) =>
        {
            if (_pomodoroFeature.IsSessionRunning)
            {
                _pomodoroFeature.Pause();
            }
            else
            {
                _pomodoroFeature.Start();
            }

            RevealPresented();
        };

        menu.Items.Add(launcherItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(timerItem);
        menu.Items.Add(stopwatchItem);
        menu.Items.Add(focusItem);

        return menu;
    }

    /// <summary>
    /// Ce qui a le droit de s'afficher. Bascules d'activation réelles :
    /// désactiver une activité libère ses écouteurs système, ce n'est pas un
    /// simple masquage.
    /// </summary>
    private MenuFlyoutSubItem BuildActivitiesMenu()
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Activités",
            Icon = new FontIcon { Glyph = "\uE9D5" }
        };

        foreach (IIslandFeature feature in _featureRegistry.Features)
        {
            var featureToggle = new ToggleMenuFlyoutItem
            {
                Text = feature.DisplayName,
                IsChecked = feature.IsEnabled
            };

            IIslandFeature captured = feature;

            featureToggle.Click += async (_, _) =>
            {
                try
                {
                    bool enabled = featureToggle.IsChecked;

                    await _featureRegistry.SetEnabledAsync(captured.Id, enabled);

                    // La bascule est enregistrée par le service : sans cela elle
                    // serait perdue au redémarrage, et l'utilisateur la prendrait
                    // pour une régression alors que l'arrêt, lui, a bien eu lieu.
                    _settingsService.SetFeatureEnabled(captured.Id, enabled);

                    // On journalise l'état obtenu, pas l'intention : activer une
                    // fonctionnalité dont l'API système est indisponible doit se
                    // voir, et non s'afficher comme un succès.
                    string outcome = captured.State switch
                    {
                        FeatureState.Running => "activée",
                        FeatureState.Stopped => "désactivée",
                        FeatureState.Faulted => "échec d'activation",
                        _ => captured.State.ToString()
                    };

                    MiniLogger.Log($"[FEATURE] {captured.DisplayName} : {outcome}");
                }
                catch (Exception ex)
                {
                    MiniLogger.Log($"[FEATURE] bascule de {captured.DisplayName} interrompue", ex);
                }
            };

            menu.Items.Add(featureToggle);
        }

        return menu;
    }

    /// <summary>Comment la notch bouge : trois caractères, et le mouvement hypnotique.</summary>
    private MenuFlyoutSubItem BuildMotionMenu()
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Mouvement",
            Icon = new FontIcon { Glyph = "\uE916" }
        };

        foreach ((MotionStyle style, string label) in new[]
                 {
                     (MotionStyle.Quiet, "Calme"),
                     (MotionStyle.Natural, "Naturel"),
                     (MotionStyle.Dynamic, "Dynamique")
                 })
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = label,
                GroupName = "NfMotionStyle",
                IsChecked = _settings.MotionStyle == style
            };

            MotionStyle captured = style;
            item.Click += (_, _) => _settingsService.Update(settings => settings.ApplyMotionStyle(captured));

            menu.Items.Add(item);
        }

        menu.Items.Add(new MenuFlyoutSeparator());

        var hypnotic = new ToggleMenuFlyoutItem
        {
            Text = "Mouvement hypnotique",
            IsChecked = _settings.AllowHypnoticMotion
        };
        hypnotic.Click += (_, _) => _settingsService.Update(settings => settings.AllowHypnoticMotion = hypnotic.IsChecked);

        menu.Items.Add(hypnotic);

        return menu;
    }

    /// <summary>À quoi la notch ressemble : le thème, sans ouvrir les réglages.</summary>
    private MenuFlyoutSubItem BuildAppearanceMenu()
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = "Apparence",
            Icon = new FontIcon { Glyph = "\uE790" }
        };

        foreach ((IslandAppearance appearance, string label) in new[]
                 {
                     (IslandAppearance.Dark, "Sombre"),
                     (IslandAppearance.Light, "Clair"),
                     (IslandAppearance.Auto, "Automatique")
                 })
        {
            var item = new RadioMenuFlyoutItem
            {
                Text = label,
                GroupName = "NfAppearance",
                IsChecked = _settings.Appearance == appearance
            };

            IslandAppearance captured = appearance;
            item.Click += (_, _) => _settingsService.Update(settings => settings.Appearance = captured);

            menu.Items.Add(item);
        }

        return menu;
    }

    // ------------------------------------------------------------------
    // Réglages
    // ------------------------------------------------------------------

    /// <summary>
    /// Réagit à une modification de préférences, d'où qu'elle vienne : la
    /// fenêtre de réglages, le menu de la zone de notification, ou un fichier
    /// édité à la main puis relu.
    /// </summary>
    private void OnSettingsChanged(object? sender, AppSettings settings)
    {
        _settings = settings;

        // Un changement de ressort est appliqué à l'animateur en place : la
        // position et la vitesse courantes sont conservées, ce qui évite un à-coup
        // pendant que l'utilisateur ajuste le curseur.
        _controller.UpdateSpringParameters(_settings.Motion, _settings.HoverMotion);

        _atmosphere.UseSpringAnimations = UseSpringAnimations();

        // Le mouvement hypnotique a pu être autorisé ou retiré : la respiration
        // de l'atmosphère est reposée au prochain rendu.
        _atmospherePreset = HypnoticPreset.None;
        _atmosphere.SetHypnoticPulse(HypnoticPreset.None, 0);

        ApplyBackdropMode();

        // La forme au repos suit la préférence : un changement de densité ou de
        // rayon doit se voir immédiatement dans l'aperçu, donc dans l'Island
        // elle-même. Le tracé mémorisé est oublié d'abord, sans quoi un rayon
        // modifié laisserait la silhouette précédente à l'écran jusqu'à ce que la
        // taille change d'elle-même.
        _shape.Forget();
        _controller.UpdateCollapsedFootprint(IslandFootprint.For(_tier, _settings.Density));
        ApplyShape(_controller.CurrentFootprint);

        // Les bascules de fonctionnalités sont appliquées sans repasser par le
        // disque : la préférence est déjà écrite.
        ApplyFeaturePreferences();

        // « S'effacer devant le plein écran » se règle désormais depuis les
        // réglages : son effet doit suivre sans attendre le prochain changement
        // de fenêtre au premier plan.
        ApplyPresence();

        Render();
    }

    private void ApplyFeaturePreferences()
    {
        foreach (IIslandFeature feature in _featureRegistry.Features)
        {
            bool desired = _settings.IsFeatureEnabled(feature.Id);

            if (feature.IsEnabled == desired)
            {
                continue;
            }

            _ = _featureRegistry.SetEnabledAsync(feature.Id, desired);
        }
    }

    /// <summary>
    /// Ouvre la fenêtre de réglages. Exposé pour que la couche de démarrage
    /// puisse l'ouvrir sur demande — l'option <c>--settings</c> — sans avoir à
    /// connaître le menu de la zone de notification.
    /// </summary>
    public void ShowSettings() => OpenSettingsWindow();

    private void OpenSettingsWindow()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_settingsWindow is null)
                {
                    _settingsWindow = new SettingsWindow(_settingsService, _featureRegistry);
                    _settingsWindow.Closed += (_, _) => _settingsWindow = null;
                }

                _settingsWindow.Activate();
            }
            catch (Exception ex)
            {
                MiniLogger.Log("[WARN] Fenêtre de réglages indisponible", ex);
            }
        });
    }

    // ------------------------------------------------------------------
    // Arrêt
    // ------------------------------------------------------------------

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;

        _geometryTimer?.Stop();
        _expirationTimer?.Stop();
        _previewEnterTimer?.Stop();
        _previewExitTimer?.Stop();

        try
        {
            // L'arrêt des fonctionnalités libère réellement leurs écouteurs
            // système et retire leurs activités : c'est la garantie symétrique du
            // démarrage.
            await _featureRegistry.DisposeAsync();
            _pluginLoader.Dispose();

            _settingsService.Changed -= OnSettingsChanged;

            foreach (IIslandSceneView scene in _scenes.Values.Distinct())
            {
                scene.ActionRequested -= OnSceneActionRequested;
            }

            _settingsWindow?.Close();
            _settingsWindow = null;

            _dropCompletionTimer?.Stop();
            _signalHypnotic?.Dispose();
            _cardHypnotic?.Dispose();
            _dropHypnotic?.Dispose();

            _clipboardMonitor.Dispose();
            _bluetoothWatcher.Dispose();
            _volumeListener.Dispose();
            _brightnessService.Dispose();
            _mediaSessionManager.Shutdown();
            _controller.Dispose();
            _diagnostics.Dispose();
            _screenWatcher.Dispose();
            _messageMonitor?.Dispose();
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[WARN] Erreur pendant l'arrêt", ex);
        }

        MiniLogger.Log("IslandWindow fermée");
        MiniLogger.Flush(TimeSpan.FromMilliseconds(400));
    }
}
