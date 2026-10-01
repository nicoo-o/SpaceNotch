using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
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
using SpaceNotch.Core.Localization;
using SpaceNotch.Features.Bluetooth;
using SpaceNotch.Features.Clipboard;
using SpaceNotch.Features.Demo;
using SpaceNotch.Features.Downloads;
using SpaceNotch.Features.FileShelf;
using SpaceNotch.Features.Launcher;
using SpaceNotch.Features.Media;
using SpaceNotch.Features.Menu;
using SpaceNotch.Features.Notifications;
using SpaceNotch.Features.Privacy;
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
using SpaceNotch.Platform.Windows.Privacy;
using SpaceNotch.Platform.Windows.Shell;
using SpaceNotch.Platform.Windows.System;
using SpaceNotch.Platform.Windows.Windowing;
using SpaceNotch_App.Animations;
using SpaceNotch_App.Composition;
using SpaceNotch_App.Controllers;
using SpaceNotch_App.Diagnostics;
using SpaceNotch_App.Views;
using SpaceNotch_App.Views.Scenes;
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
    private readonly NoteFeature _noteFeature;
    private readonly SpaceNotch.Features.Power.ChargeFeature _chargeFeature;
    private readonly SpaceNotch.Features.Power.SystemMonitorFeature _monitorFeature;
    private readonly SpaceNotch.Features.Calendar.MeetingFeature _meetingFeature;
    private readonly SpaceNotch.Features.Weather.WeatherFeature _weatherFeature;
    private readonly SpaceNotch.Features.Share.ShareFeature _shareFeature;
    /// <summary>Visite (--tour) : 0 = Windows décide, 1 = calme forcé, 2 = calme levé.</summary>
    private volatile int _quietOverride;
    private readonly LauncherFeature _launcherFeature;
    private readonly QuickMenuFeature _quickMenuFeature;
    private readonly ClipboardFeature _clipboardFeature;
    private readonly NotificationFeature _notificationFeature;
    private readonly WelcomeFeature _welcomeFeature;
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

    /// <summary>
    /// Forme au repos ajustée au contenu présenté : la notch s'élargit ou se
    /// resserre avec son texte, comme dans la référence.
    /// </summary>
    private IslandFootprint _restFootprint = IslandFootprint.Idle;
    private string? _fitSlackFor;
    private double _fitSlack;

    /// <summary>Préréglage hypnotique en cours et instant où il a commencé, pour l'apaisement.</summary>
    private HypnoticPreset _runningPreset = HypnoticPreset.None;
    private DateTimeOffset _runningSince = DateTimeOffset.UtcNow;

    /// <summary>Minuteur unique de l'apaisement : il ne tourne que pendant une boucle compacte.</summary>
    private DispatcherQueueTimer? _attenuationTimer;

    /// <summary>Dernière annonce faite à Narrateur : activité et titre.</summary>
    private string _announcedKey = string.Empty;

    /// <summary>Racine de la scène ouverte affichée, pour ne jouer son entrée qu'une fois.</summary>
    private FrameworkElement? _visibleSceneRoot;

    /// <summary>Octets de la pochette compacte affichée, pour ne la décoder qu'au changement.</summary>
    private byte[]? _restArtworkBytes;

    /// <summary>Visibilité des vues de repos avant le rendu en cours.</summary>
    private bool _signalWasVisible;
    private bool _cardWasVisible;

    /// <summary>
    /// Textes de mesure, hors de l'arbre visuel : ils portent les styles des
    /// textes affichés et servent à connaître une largeur sans rien afficher.
    /// </summary>
    private readonly TextBlock _measureSignal = new();
    private readonly TextBlock _measureSubhead = new();
    private readonly TextBlock _measureHeadline = new();
    private readonly TextBlock _measureMetric = new();
    private readonly TextBlock _measureStack = new();

    /// <summary>Dernier roulement de mesure, en millisecondes système.</summary>
    private long _lastRoll;

    /// <summary>Épaule appliquée en dernier à la zone de contenu, pour ne la redisposer qu'au changement.</summary>
    private double _contentShoulder = double.NaN;

    /// <summary>
    /// Matière hypnotique des paliers signal et carte, et de la cible de dépôt.
    /// <c>null</c> si le compositeur l'a refusée : le glyphe fixe reste alors.
    /// </summary>
    private HypnoticSurface? _signalHypnotic;
    private HypnoticSurface? _cardHypnotic;
    private HypnoticSurface? _tabHypnotic;
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
    private readonly HashSet<DispatcherQueueTimer> _ownedTimers = [];

    private SettingsWindow? _settingsWindow;
    private IDisposable? _notificationPixelSubscription;

    private SystemVisualState _visualState = SystemVisualState.Permissive;
    private bool _isClosed;

    /// <summary>Faux tant que l'Island est retirée devant le plein écran : la bulle se retire avec elle.</summary>
    private bool _islandShown = true;

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

        // Le bord et la position enregistrés : la notch redémarre là où elle a
        // été accrochée la dernière fois (ADR-020).
        LoadDock();

        _hWnd = WindowNative.GetWindowHandle(this);
        var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        AppIcon.ApplyTo(_appWindow);

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
        GlyphView.AnimationsEnabled = _visualState.UseSpringAnimations;

        // La couche décorative est créée avant le contrôleur : celui-ci applique
        // sa géométrie initiale dès sa construction (SnapTo), et la géométrie
        // doit pouvoir positionner les deux surfaces.
        _atmosphere = new AtmosphereWindow();
        CreateBubble();

        // La préférence est lue par clé de fonctionnalité : la fenêtre n'associe
        // donc pas elle-même une fonctionnalité à un réglage, elle demande.
        _shelfManager = new FileShelfManager(
            _activityManager, _eventBus, _settings.IsFeatureEnabled(FileShelfManager.FeatureKey));

        _pomodoroFeature = new PomodoroFeature(
            _activityManager, _eventBus, _settings.IsFeatureEnabled(PomodoroFeature.FeatureKey));

        _timerFeature = new TimerFeature(_activityManager, _eventBus);
        _noteFeature = new NoteFeature(
            _activityManager,
            _eventBus,
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SpaceNotch"));
        _launcherFeature = new LauncherFeature(
            _activityManager,
            _eventBus,
            store: new SpaceNotch_App.Launcher.SettingsLauncherHistoryStore(_settingsService))
        {
            WebSearchEngine = _settings.WebSearchEngine
        };

        _quickMenuFeature = new QuickMenuFeature(_activityManager, _eventBus);
        _launcherFeature.CommandInvoked += (kind, value) => OnUiThread(() => RunCommand(kind, value));
        _meetingFeature = new SpaceNotch.Features.Calendar.MeetingFeature(
            _activityManager, _eventBus, new SpaceNotch.Platform.Windows.Calendar.CalendarReader(),
            _settings.IsFeatureEnabled(SpaceNotch.Features.Calendar.MeetingFeature.FeatureKey));
        _weatherFeature = new SpaceNotch.Features.Weather.WeatherFeature(_activityManager, _eventBus, _settings.WeatherCity);
        _weatherFeature.Changed += () => OnUiThread(RequestRender);
        _shareFeature = new SpaceNotch.Features.Share.ShareFeature(_activityManager, _eventBus);
        _chargeFeature = new SpaceNotch.Features.Power.ChargeFeature(
            _activityManager, _eventBus, new SpaceNotch.Platform.Windows.Power.PowerWatcher(),
            _settings.IsFeatureEnabled(SpaceNotch.Features.Power.ChargeFeature.FeatureKey));
        _monitorFeature = new SpaceNotch.Features.Power.SystemMonitorFeature(
            _activityManager, _eventBus,
            _settings.IsFeatureEnabled(SpaceNotch.Features.Power.SystemMonitorFeature.FeatureKey));
        _welcomeFeature = new WelcomeFeature(_activityManager, _eventBus);
        _welcomeFeature.Completed += (_, _) => OnWelcomeCompleted();

        _notificationListener.Log = message => MiniLogger.Log(message);
        _notificationFeature = new NotificationFeature(
            _activityManager, _eventBus, _notificationListener,
            _settings.IsFeatureEnabled(NotificationFeature.FeatureKey),
            isQuiet: () => _quietOverride switch { 1 => true, 2 => false, _ => SpaceNotch.Platform.Windows.Notifications.FocusAssistProbe.IsQuiet() })
        {
            IgnoredApps = _settings.IgnoredNotificationApps
        };

        _clipboardFeature = new ClipboardFeature(
            _activityManager, _eventBus, _clipboardMonitor, _hWnd,
            _settings.IsFeatureEnabled(ClipboardFeature.FeatureKey))
        {
            IgnoreSecrets = _settings.ClipboardIgnoreSecrets
        };

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
            _notificationFeature,
            new BluetoothFeature(
                _activityManager, _eventBus, _bluetoothWatcher,
                _settings.IsFeatureEnabled(BluetoothFeature.FeatureKey)),
            _pomodoroFeature,
            _shelfManager,
            new BrightnessHudFeature(
                _activityManager, _eventBus, _brightnessService,
                _settings.IsFeatureEnabled(BrightnessHudFeature.FeatureKey)),
            _timerFeature,
            _noteFeature,
            _launcherFeature,
            _quickMenuFeature,
            _welcomeFeature,
            new DownloadsFeature(
                _activityManager, _eventBus, KnownFolders.Downloads,
                _settings.IsFeatureEnabled(DownloadsFeature.FeatureKey)),
            new PrivacyFeature(
                _activityManager, _eventBus, new CapabilityUsageWatcher(),
                _settings.IsFeatureEnabled(PrivacyFeature.FeatureKey)),
            _clipboardFeature,
            _chargeFeature,
            _monitorFeature,
            _meetingFeature,
            _weatherFeature,
            _shareFeature,
            CreateChannelFeature()
        };

        WireWave6b();

        // Les greffons sont chargés avant la création du registre : ils en font
        // partie dès le démarrage et bénéficient donc exactement du même cycle de
        // vie, des mêmes bascules et du même routage d'actions que les
        // fonctionnalités intégrées.
        _pluginLoader = new PluginLoader();

        // Seuls les greffons approuvés (nom et empreinte) sont chargés ; les
        // autres attendent dans Réglages › À propos.
        PluginLoadOptions pluginOptions = new(
#if SPACENOTCH_RELEASE
            RequireAuthenticodeSignature: true
#else
            RequireAuthenticodeSignature: false
#endif
        );

        PluginLoadResult plugins = _pluginLoader.LoadAll(
            new IslandFeatureContext(_activityManager, _eventBus),
            new PluginAllowlist(_settings.ApprovedPlugins),
            pluginOptions);

        foreach (string pending in plugins.Pending ?? [])
        {
            MiniLogger.Log($"[PLUGIN] En attente d'approbation : {System.IO.Path.GetFileName(pending)}");
        }

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
            UsesSideTab
                ? SideTab.Rest(_settings.TabSize, _settings.SideShoulderRadius)
                : IslandFootprint.For(IslandPresentation.Resolve(null), _settings.Density),
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

        _measureSignal.Style = SignalLabel.Style;
        _measureSubhead.Style = CardSubhead.Style;
        _measureHeadline.Style = CardHeadline.Style;
        SignalLabel.IsTextTrimmedChanged += (label, _) => CatchUpTrimmed(label);
        CardSubhead.IsTextTrimmedChanged += (label, _) => CatchUpTrimmed(label);
        CardHeadline.IsTextTrimmedChanged += (label, _) => CatchUpTrimmed(label);
        WireEvents();
        WireSceneActions();
        StartMagnet();
        SceneTabs.TabInvoked += (_, id) => _controller.PresentActivity(id);
        ApplyBackdropMode();

        // Toute modification venue de la fenêtre de réglages est appliquée ici,
        // par le même chemin que les préférences du menu : il n'existe donc pas
        // deux manières d'appliquer un réglage.
        _settingsService.Changed += OnSettingsChanged;

        SetupTrayIcon();

        _specularHeight = ResolveSpecularHeight();

        // Géométrie initiale : appliquée sans animation, l'Island démarre au repos.
        ApplyLayout();
        ApplyGeometry(_controller.CurrentFootprint);

        // Sans activation : l'Island ne prend jamais le premier plan à
        // l'application de l'utilisateur en apparaissant.
        _appWindow.Show(activateWindow: false);
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
        _tabHypnotic = HypnoticSurface.TryAttach(TabHypnoticHost);
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
    /// Donne à un emplacement de glyphe sa grille hypnotique, ou son glyphe.
    /// Jamais les deux : la grille remplace l'icône, elle ne s'y superpose pas.
    /// La couleur appartient au préréglage — bleu pour lire, orange pour
    /// réfléchir, dérive pêche → lavande pour construire — comme dans la
    /// référence : elle dit ce qui se passe.
    /// </summary>
    private void ApplyHypnoticSlot(
        HypnoticSurface? surface,
        FrameworkElement host,
        FrameworkElement glyph,
        HypnoticPreset preset)
    {
        bool hypnotic = surface is not null && preset != HypnoticPreset.None;

        host.Visibility = hypnotic ? Visibility.Visible : Visibility.Collapsed;
        glyph.Visibility = hypnotic ? Visibility.Collapsed : Visibility.Visible;

        surface?.SetPreset(hypnotic ? preset : HypnoticPreset.None, AnimateHypnotic() && !HypnoticResting());
    }

    /// <summary>
    /// Préréglage de repos d'une activité, avec la mémoire de son départ : un
    /// nouveau préréglage — ou un regard de l'utilisateur — relance le compte de
    /// l'apaisement.
    /// </summary>
    private HypnoticPreset RestingPreset(IslandActivity activity)
    {
        HypnoticPreset preset = HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);
        bool looking = _controller.State is IslandState.Preview or IslandState.Expanding or IslandState.Expanded;

        if (preset != _runningPreset || looking)
        {
            _runningPreset = preset;
            _runningSince = DateTimeOffset.UtcNow;
        }

        ArmAttenuation();

        return preset;
    }

    /// <summary>
    /// Vrai lorsqu'une boucle compacte tourne depuis assez longtemps pour se
    /// figer. Elle a dit « ça travaille » dès sa première seconde ; au-delà, le
    /// mouvement n'apprend plus rien et devient une distraction (WCAG 2.2.2).
    /// </summary>
    private bool HypnoticResting()
        => HypnoticAttenuation.ShouldRest(
            _runningPreset,
            DateTimeOffset.UtcNow - _runningSince,
            NotchPresentationResolver.Resolve(_controller.State, _controller.PresentedActivity));

    /// <summary>
    /// Arme l'unique minuteur de l'apaisement pour l'instant où la boucle doit
    /// se figer — et seulement si une boucle tourne.
    /// </summary>
    private void ArmAttenuation()
    {
        if (!HypnoticField.IsLooping(_runningPreset) || HypnoticResting())
        {
            _attenuationTimer?.Stop();
            return;
        }

        TimeSpan remaining = HypnoticAttenuation.Delay - (DateTimeOffset.UtcNow - _runningSince);

        _attenuationTimer ??= CreateOneShotTimer(HypnoticAttenuation.Delay, RequestRender);
        _attenuationTimer.Interval = remaining > TimeSpan.FromMilliseconds(50) ? remaining : TimeSpan.FromMilliseconds(50);
        _attenuationTimer.Stop();
        _attenuationTimer.Start();
    }

    /// <summary>Arrête la matière des paliers de repos, qui ne sont plus visibles.</summary>
    private void StopRestingHypnotic()
    {
        _signalHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        _cardHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        _tabHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
    }

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
        _scenes[IslandSceneCatalog.QuickMenu] = QuickMenuSceneView;
        _scenes[IslandSceneCatalog.Bluetooth] = BluetoothSceneView;
        _scenes[IslandSceneCatalog.Welcome] = WelcomeSceneView;
        _scenes[IslandSceneCatalog.Color] = ColorSceneView;
        _scenes[IslandSceneCatalog.Note] = NoteSceneView;
        _scenes[IslandSceneCatalog.Quiet] = QuietSceneView;
        _scenes[IslandSceneCatalog.Monitor] = MonitorSceneView;
        _scenes[IslandSceneCatalog.Share] = ShareSceneView;

        // Luminosité et volume partagent la même vue : leur charge utile est
        // identique, seule la clé d'icône les distingue.
        _scenes[IslandSceneCatalog.BrightnessHud] = VolumeSceneView;

        // Scènes sans vue dédiée : elles partagent la vue générique, qui rend le
        // titre, le sous-titre et les contrôles déclarés. « Card » est la clé
        // destinée au contenu tiers — c'est par elle qu'un greffon s'affiche sans
        // que la fenêtre ait à le connaître.
        foreach (string fallbackKey in new[]
                 {
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

            // Les commandes du menu rapide touchent la fenêtre : elles sont
            // exécutées ici, pas par une fonctionnalité.
            if (HandleQuickMenuAction(request) || await HandleWelcomeActionAsync(request))
            {
                return;
            }

            bool handled = await _featureRegistry.HandleActionAsync(request);

            if (!handled)
            {
                MiniLogger.Log($"[ACTION] aucune fonctionnalité n'a traité {request.ActionId}");
                return;
            }

            // Une action qui a modifié l'affichage doit être reprojetée : la
            // fonctionnalité a pu republier son activité, mais elle a aussi pu se
            // contenter d'agir — le rendu est alors rafraîchi sans attendre. La
            // recherche, elle, republie toujours : un second rendu par lettre
            // était du travail pour rien.
            if (request.ActionId != LauncherScene.SearchAction)
            {
                Render();
            }
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
        _controller.AnimationCompleted += (_, _) =>
        {
            RequestRender();
            DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                CatchUpTrimmed(SignalLabel);
                CatchUpTrimmed(CardHeadline);
                CatchUpTrimmed(CardSubhead);
            });
        };
        _controller.StateChanged += (_, state) =>
        {
            // Ouverte, l'activité présentée est épargnée ; refermée, elle
            // reprend son échéance. Le minuteur se recale dans les deux cas.
            RearmExpirationTimer();

            // Refermée, la notch rend le clavier à l'application de l'utilisateur.
            if (state == IslandState.Closed && _typingCapture)
            {
                _typingCapture = false;
                WindowChrome.SetKeyboardCapture(_hWnd, enabled: false);
            }

            // La note se range à la fermeture ; son texte est déjà enregistré.
            if (state == IslandState.Closed && _noteFeature.IsShown)
            {
                _noteFeature.Dismiss();
            }

            // Le lanceur ne vit que tant qu'il est ouvert — même quand une autre
            // scène (le menu rapide) l'a remplacé avant la fermeture : sinon il
            // restait dans la pile et la pastille affichait « Rechercher ».
            if (state == IslandState.Closed && _launcherFeature.IsShown)
            {
                _launcherFeature.Dismiss();
            }

            // Refermer la notch pendant la présentation, c'est la passer.
            if (state == IslandState.Closed && _welcomeFeature.IsShown)
            {
                _welcomeFeature.Finish();
            }

            // Le menu rapide aussi : refermé, il ne reste pas en tête de pile.
            if (state == IslandState.Closed && _quickMenuFeature.IsShown)
            {
                _quickMenuFeature.Dismiss();
                _activityManager.PinPresentation(null);
            }

            RequestRender();
        };

        _activityManager.ActiveActivityChanged += (_, _) => OnUiThread(RearmExpirationTimer);
        _activityManager.ActivityRemoved += (_, _) => OnUiThread(RearmExpirationTimer);

        // Les défaillances d'abonnés ne sont plus silencieuses.
        _eventBus.HandlerFailed = (eventType, exception) =>
            MiniLogger.Log($"[BUS] Abonné défaillant pour {eventType.Name}", exception);

        // Les notifications du système (ainsi que les alertes métier publiées
        // sur le même contrat) surprennent Pixel, mais uniquement lorsqu'il est
        // visible. L'événement peut venir d'un fil de rappel Windows.
        _notificationPixelSubscription = _eventBus.Subscribe<NotificationPostedEvent>(_ => OnUiThread(() =>
        {
            if (!_isClosed && PixelAtRest && RestEyes.Visibility == Visibility.Visible)
            {
                SurprisePixel();
            }
        }));

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

        // Le raccourci global ouvre la recherche de n'importe où : Alt+Espace,
        // ou Win+Maj+Espace si une autre application tient déjà le premier.
        _launcherFeature.Hotkey = SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.RegisterLauncher(_hWnd);
        MiniLogger.Log($"Raccourci de recherche : {_launcherFeature.Hotkey ?? "aucun (les deux sont pris)"}");

        _shelfManager.ShareRequested += path => OnUiThread(() => _shareFeature.Share(path));
        _shelfManager.ShelfUpdated += (_, _) =>
            OnUiThread(() => FileShelfSceneView.UpdateItems(_shelfManager.GetItems()));

        // Les contrôles de la scène média ne sont plus câblés ici : la scène
        // déclare ses actions, la fenêtre les route vers la fonctionnalité
        // propriétaire. La fenêtre n'appelle donc plus directement la session
        // média, ce qui était exactement le couplage qu'il fallait retirer.
        NotificationSceneView.DismissRequested += () => _controller.RequestCollapse();
        HookInk();

        Closed += OnWindowClosed;
        Activated += OnWindowActivated;
    }

    /// <summary>
    /// Clic à l'extérieur : la notch ouverte se referme.
    ///
    /// Un clic sur la notch la rend active — la saisie clavier y est permise tant
    /// que le pointeur la désigne. Cliquer ailleurs la désactive : c'est ce
    /// signal, fourni par Windows, qui referme la notch, sans crochet de souris
    /// global et sans scrutation.
    /// </summary>
    private void OnWindowActivated(object sender, WindowActivatedEventArgs args)
    {
        if (args.WindowActivationState == WindowActivationState.Deactivated
            && _controller.State is IslandState.Expanded or IslandState.Expanding)
        {
            _controller.RequestCollapse();
        }
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

        UpdateBubble();

        // Ce qui a changé depuis le rendu précédent décide de la transition :
        // une ouverture, une fermeture, une autre activité, l'entrée dans l'aperçu.
        bool wasShowingScene = _visibleSceneRoot is not null;
        bool changedActivity = activity is not null
            && _lastPresentedId is not null
            && !string.Equals(activity.Id, _lastPresentedId, StringComparison.Ordinal);
        IslandState previousState = _lastRenderedState;
        _lastPresentedId = activity?.Id;
        Celebrate(activity);
        UpdateTabs(activity, expanded);
        UpdateFocusTrace();
        _lastRenderedState = _controller.State;

        // Le palier au repos ne dépend jamais de l'ouverture : il est résolu à
        // chaque rendu et mémorisé, parce que la fermeture doit retrouver
        // exactement la forme quittée et que le survol doit savoir quoi annoncer.
        _tier = IslandPresentation.Resolve(activity);
        _restFootprint = FitRest(activity, _tier);
        _controller.UpdateCollapsedFootprint(_restFootprint);

        // Seules les scènes qui ne sont pas la cible sont repliées. Replier puis
        // réafficher la scène visible faisait perdre le focus à ce qu'elle
        // contient : chaque lettre tapée dans la recherche du lanceur la vidait
        // de son focus.
        FrameworkElement? targetRoot = expanded
            && activity is not null
            && _scenes.TryGetValue(activity.SceneKey, out IIslandSceneView? targetScene)
            ? targetScene.Root
            : null;

        foreach (FrameworkElement root in _sceneRoots)
        {
            if (!ReferenceEquals(root, targetRoot))
            {
                root.Visibility = Visibility.Collapsed;
            }
        }

        // Ce qui était visible avant ce rendu : un texte remplacé sur une vue qui
        // reste affichée se transforme sur place, au lieu de réapparaître.
        _signalWasVisible = SignalRestView.Visibility == Visibility.Visible;
        _cardWasVisible = CardRestView.Visibility == Visibility.Visible;

        // Passage de la forme compacte à la scène ouverte : la position des
        // éléments compacts est relevée maintenant, avant qu'ils ne soient
        // masqués, pour que la scène les fasse grandir depuis leur place.
        Dictionary<MorphAnchorKind, MorphRect>? morph = expanded && (_signalWasVisible || _cardWasVisible)
            ? CaptureRestAnchors()
            : null;

        IdleRestView.Visibility = Visibility.Collapsed;
        SignalRestView.Visibility = Visibility.Collapsed;
        CardRestView.Visibility = Visibility.Collapsed;
        TabRestView.Visibility = Visibility.Collapsed;
        ShowRestPixel(false);
        ShowRestLife(atRest: false);

        // Clawd ne bat que là où il est montré : la branche qui le montre le rallume.
        SignalClawd.Visibility = Visibility.Collapsed;
        CardClawd.Visibility = Visibility.Collapsed;

        UpdateStackIndicator();
        Announce(activity);
        ApplyActivityTint(activity);
        ApplyStateTint(activity);
        CrenelOnError(activity);

        if (activity is null)
        {
            StopRestingHypnotic();
            SceneTrame.Present(null, null, music: false);
            IdleRestView.Visibility = Visibility.Visible;

            // L'heure est un réglage et non un défaut : elle installerait une
            // horloge à la minute dans un produit dont la promesse est de ne rien
            // faire au repos. Sans elle, la lèvre est vide — le point de veille
            // ne l'accompagne que pour lui donner un repère.
            IdleClock.Visibility = _settings.ShowClockAtRest && !UsesSideTab
                ? Visibility.Visible
                : Visibility.Collapsed;

            // Pixel remplace le point de veille : deux repères au même endroit
            // se feraient concurrence.
            IdleStatusDot.Visibility = PixelAtRest ? Visibility.Collapsed : IdleClock.Visibility;
            ShowRestPixel(PixelAtRest);
            ShowRestWeather();
            IdleClock.Animate = UseSpringAnimations();
            IdleClock.Show(_tourClock ?? DateTime.Now.ToString("HH:mm", System.Globalization.CultureInfo.InvariantCulture));
            ShowRestLife(atRest: true);
            ArmClockTick(IdleClock.Visibility == Visibility.Visible);
            return;
        }

        bool known = _scenes.TryGetValue(activity.SceneKey, out IIslandSceneView? scene);

        if (expanded && known && scene is not null)
        {
            StopRestingHypnotic();

            if (scene is InfoScene generic)
            {
                generic.AnimateHypnotic = AnimateHypnotic();
                generic.ClawdStyle = _settings.ClawdStyle;
            }
            else if (scene is VolumeHudScene hud)
            {
                hud.AnimateValues = UseSpringAnimations();
            }
            else if (scene is ClipboardScene clipboard)
            {
                clipboard.AnimateSwipe = UseSpringAnimations();
            }

            scene.Apply(activity);
            scene.Root.Visibility = Visibility.Visible;
            PresentTrame(activity, scene);

            // Entrée de la scène, une seule fois : son contenu apparaît sur place
            // pendant que la forme grandit, et les éléments ancrés grandissent
            // depuis la forme compacte.
            if (!ReferenceEquals(_visibleSceneRoot, scene.Root))
            {
                _visibleSceneRoot = scene.Root;

                // Le lanceur s'ouvre pour qu'on tape : la fenêtre prend le
                // clavier et le champ le focus, sans attendre un survol.
                if (scene is LauncherScene launcher)
                {
                    CaptureKeyboardForTyping();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, launcher.FocusSearch);
                }
                else if (scene is NoteScene note)
                {
                    // La note s'ouvre pour qu'on écrive : clavier et curseur tout de suite.
                    CaptureKeyboardForTyping();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, note.FocusNote);
                }
                else if (scene is WelcomeScene welcome)
                {
                    // Entrée avance, Échap passe : la présentation se suit au clavier.
                    CaptureKeyboardForTyping();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, welcome.FocusPrimary);
                }
                else if (scene is QuickMenuScene menu)
                {
                    // Le menu se parcourt aussi au clavier : ↑↓, Entrée, Échap.
                    CaptureKeyboardForTyping();
                    DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, menu.FocusFirst);
                }

                // Une seule ligne de temps : la forme grandit, puis le contenu
                // arrive, flou, et se précise.
                ContentTransition.Play(scene.Root, UseSpringAnimations(), TimeSpan.FromMilliseconds(80));
                PlayVeil(TimeSpan.FromMilliseconds(40));

                if (morph is not null)
                {
                    MorphPlayer.PlayWhenLaidOut(morph, scene, ContentArea, UseSpringAnimations());
                }
            }

            return;
        }

        _visibleSceneRoot = null;
        InfoSceneView.Rest();
        SceneTrame.Present(null, null, music: false);

        PresentResting(activity);

        if (wasShowingScene)
        {
            // Fermeture : la scène est partie avec la forme, la forme compacte
            // revient floue et se précise pendant que la notch se referme.
            PlayVeil();

            if (VisibleRestView() is { } rest)
            {
                ContentTransition.Play(rest, UseSpringAnimations(), TimeSpan.FromMilliseconds(60));
            }
        }
        else if (changedActivity)
        {
            // Une activité en remplace une autre : la forme respire, le contenu
            // se découvre sous une vague de pixels (A2) — sous le voile quand la
            // forme n'est pas celle du bord ou que les animations sont réduites.
            Breathe();

            if (!PlayDissolve(expanded ? _controller.Opened(activity!) : _restFootprint))
            {
                PlayVeil();
            }
        }
        else if (_controller.State == IslandState.Preview
            && previousState == IslandState.Closed
            && VisibleRestView() is { } previewed)
        {
            // L'aperçu : le contenu suit la forme qui s'avance.
            SlideWithGrowth(previewed);
        }

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

        HypnoticPreset preset = RestingPreset(activity);

        // Accrochée à un côté, la forme de repos est une languette : l'icône
        // ou la grille, et la jauge. Le texte attend l'ouverture.
        if (UsesSideTab)
        {
            PresentTab(activity, preset);
            return;
        }

        CompactTrailing trailing = CompactTrailing.For(activity);
        string? metric = CompactTrailing.MetricFor(activity, trailing);
        Visibility metricVisibility = metric is null ? Visibility.Collapsed : Visibility.Visible;

        if (shown == IslandPresentationTier.Signal)
        {
            SignalGlyph.Key = activity.IconKey;
            PlayArrival(activity, SignalGlyph);
            SetText(SignalLabel, activity.Title, _signalWasVisible, veil: true);
            ShimmerText.Set(SignalLabel, activity.MotionState == ActivityMotionState.Working, UseSpringAnimations());
            ShimmerText.Set(CardHeadline, working: false, animate: false);
            SetText(SignalMetric, metric, _signalWasVisible, metric: true);
            SignalMetric.Visibility = metricVisibility;

            SignalLevel.Visibility = trailing.Kind == TrailingKind.Level ? Visibility.Visible : Visibility.Collapsed;
            SignalLevelScale.ScaleX = trailing.Kind == TrailingKind.Level ? trailing.Value : 0;
            SignalTrailing.Show(trailing);
            SignalRestView.Visibility = Visibility.Visible;

            _cardHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
            ApplyHypnoticSlot(_signalHypnotic, SignalHypnoticHost, SignalGlyph, preset);
            ApplyRestArtwork(activity, SignalArtwork, SignalArtworkImage, SignalGlyph, preset);
            CardArtwork.Visibility = Visibility.Collapsed;
            ApplyClawd(activity, SignalClawd, SignalClawdPitch, _signalHypnotic, SignalHypnoticHost, SignalGlyph, SignalArtwork);
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

        CardGlyph.Key = activity.IconKey;
        PlayArrival(activity, CardGlyph);

        // Le contexte d'abord, l'état ensuite : une activité qui déclare une
        // ligne de contexte — « Read app-sidebar.tsx · 219 lines » — la voit à
        // la place de la légende calculée. Un texte qui change sur une carte
        // déjà visible se remplace sur place, par un fondu : la carte reste.
        SetText(CardSubhead, SubheadFor(activity), _cardWasVisible);
        SetText(CardHeadline, activity.Title, _cardWasVisible, veil: true);
        ShimmerText.Set(CardHeadline, activity.MotionState == ActivityMotionState.Working, UseSpringAnimations());
        ShimmerText.Set(SignalLabel, working: false, animate: false);
        SetText(CardMetric, metric, _cardWasVisible, metric: true);
        CardMetric.Visibility = metricVisibility;
        CardTrailing.Show(trailing);
        CardRestView.Visibility = Visibility.Visible;
        PresentHeap(activity);

        _signalHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        ApplyHypnoticSlot(_cardHypnotic, CardHypnoticHost, CardGlyph, preset);
        ApplyRestArtwork(activity, CardArtwork, CardArtworkImage, CardGlyph, preset);
        SignalArtwork.Visibility = Visibility.Collapsed;
        ApplyClawd(activity, CardClawd, CardClawdPitch, _cardHypnotic, CardHypnoticHost, CardGlyph, CardArtwork);
    }

    /// <summary>
    /// Sablier de pixels (P4) : un travail en cours qui dit son avancement, ou
    /// un téléchargement qui dit ses octets, empile ses pixels au fond de la carte.
    /// </summary>
    private void PresentHeap(IslandActivity activity)
    {
        double? percent = activity.MotionState != ActivityMotionState.Working
            ? null
            : activity.Progress is { } progress
                ? Math.Clamp(progress, 0, 1) * 100
                : (activity.Payload as BytesPayload)?.HeapPercent;

        if (percent is null)
        {
            CardHeap.Clear();
            return;
        }

        CardHeap.Animate = UseSpringAnimations();
        CardHeap.Fill(activity.Id, percent.Value, StatePalette.Brush(activity.State));
    }

    /// <summary>
    /// Trame de la scène ouverte, dans la couleur de l'activité. Ni en thème
    /// clair ni en contraste élevé ; fixe quand Windows réduit les animations.
    /// Dans le lecteur, elle suit la musique.
    /// </summary>
    private void PresentTrame(IslandActivity activity, IIslandSceneView scene)
    {
        SceneTrame.IsAllowed = _settings.ShowTrame && !_visualState.HighContrast && _settings.Appearance != IslandAppearance.Light;
        SceneTrame.Animate = UseSpringAnimations();

        Color tint = DeclaredTint(activity) ?? StatePalette.Tint(activity.State);

        SceneTrame.Present(scene.Root, tint, music: scene is MediaExpandedScene && activity.State == IslandActivityState.MediaActive);
    }

    /// <summary>Languette latérale au repos : glyphe ou grille, jauge verticale.</summary>
    private void PresentTab(IslandActivity activity, HypnoticPreset preset)
    {
        TabGlyph.Key = activity.IconKey;
        TabGlyph.Tint = StatePalette.Brush(activity.State);

        double? progress = activity.Progress;
        TabLevel.Visibility = progress is null ? Visibility.Collapsed : Visibility.Visible;
        TabLevelFill.Height = TabLevel.Height * Math.Clamp(progress ?? 0, 0, 1);

        _signalHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        _cardHypnotic?.SetPreset(HypnoticPreset.None, animate: false);
        ApplyHypnoticSlot(_tabHypnotic, TabHypnoticHost, TabGlyph, preset);

        SignalArtwork.Visibility = Visibility.Collapsed;
        CardArtwork.Visibility = Visibility.Collapsed;
        TabRestView.Visibility = Visibility.Visible;

        AutomationProperties.SetName(TabRestView, activity.Title ?? string.Empty);
    }

    /// <summary>
    /// Relève la place des éléments de la forme compacte visible, dans le repère
    /// de la zone de contenu.
    /// </summary>
    private Dictionary<MorphAnchorKind, MorphRect> CaptureRestAnchors()
    {
        var anchors = new Dictionary<MorphAnchorKind, MorphRect>();

        bool signal = _signalWasVisible;

        FrameworkElement artwork = signal ? SignalArtwork : CardArtwork;
        FrameworkElement hypnotic = signal ? SignalHypnoticHost : CardHypnoticHost;
        FrameworkElement glyph = signal ? SignalGlyph : CardGlyph;
        FrameworkElement title = signal ? SignalLabel : CardHeadline;

        if (MorphPlayer.Measure(artwork, ContentArea) is { } art)
        {
            anchors[MorphAnchorKind.Artwork] = art;
        }
        else if ((MorphPlayer.Measure(hypnotic, ContentArea) ?? MorphPlayer.Measure(glyph, ContentArea)) is { } icon)
        {
            anchors[MorphAnchorKind.Icon] = icon;
        }

        if (MorphPlayer.Measure(title, ContentArea) is { } text)
        {
            anchors[MorphAnchorKind.Title] = text;
        }

        return anchors;
    }

    /// <summary>
    /// Pochette de la forme compacte : montrée à la place du glyphe lorsque
    /// l'activité en porte une et qu'aucune grille hypnotique ne l'occupe.
    /// </summary>
    private void ApplyRestArtwork(IslandActivity activity, FrameworkElement slot, Image image, FrameworkElement glyph, HypnoticPreset preset)
    {
        bool show = activity.Artwork is { Length: > 0 } && (preset == HypnoticPreset.None || !AnimateHypnoticAvailable());

        slot.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

        if (!show)
        {
            return;
        }

        glyph.Visibility = Visibility.Collapsed;

        if (!ReferenceEquals(activity.Artwork, _restArtworkBytes) || image.Source is null)
        {
            _restArtworkBytes = activity.Artwork;
            _ = LoadRestArtworkAsync(image, activity.Artwork);
        }
    }

    /// <summary>Vrai si une grille hypnotique peut s'afficher : le compositeur l'a acceptée.</summary>
    private bool AnimateHypnoticAvailable() => _signalHypnotic is not null && _cardHypnotic is not null;

    private static async Task LoadRestArtworkAsync(Image image, byte[]? bytes)
    {
        try
        {
            image.Source = await ArtworkLoader.LoadAsync(bytes);
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[ARTWORK] pochette compacte illisible", ex);
        }
    }

    /// <summary>
    /// Écrit un texte, et joue la transition de contenu s'il remplace un texte
    /// déjà visible.
    /// </summary>
    private void SetText(TextBlock target, string? value, bool visible, bool veil = false, bool metric = false)
    {
        string next = value ?? string.Empty;

        if (string.Equals(ScrambleText.FinalOf(target), next, StringComparison.Ordinal))
        {
            return;
        }

        // Un titre qui change vraiment se décode (A1) ; les autres textes changent sur place.
        if (veil && visible && ScrambleText.Set(target, next, UseSpringAnimations()))
        {
            return;
        }

        target.Text = next;

        // Une mesure qui défile — un pourcentage publié plusieurs fois par
        // seconde — change sur place : la faire réapparaître en fondu à chaque
        // valeur la faisait clignoter sans arrêt.
        // Une mesure roule — au plus cinq fois par seconde : au-delà, un
        // pourcentage publié en continu tremblerait au lieu de rouler.
        if (visible && metric)
        {
            long now = Environment.TickCount64;

            if (now - _lastRoll >= 200)
            {
                _lastRoll = now;
                ContentTransition.Roll(target, UseSpringAnimations());
            }

            return;
        }

        if (visible && !metric)
        {
            ContentTransition.Play(target, UseSpringAnimations(), TimeSpan.FromMilliseconds(60), ContentTransition.SwapDuration);

            // Un titre qui change se lit flou, puis net ; une mesure qui défile
            // — une taille reçue, un pourcentage — change sans voile.
            if (veil)
            {
                PlayVeil();
            }
        }
    }

    /// <summary>
    /// Hauteur des deux lignes d'une carte : légende 14, écart 2, titre 16. Voir
    /// <see cref="IslandFootprint.CardVerticalPadding"/>.
    /// </summary>
    private const double CardContentHeight = 14 + 2 + 16;

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

        // Une seule information par ligne : le détail s'il existe, sinon l'état.
        // « Connecté · Batterie 84 % · Périphérique » disait trois fois la même
        // chose que l'icône et l'arc de batterie.
        if (detail.Length == 0)
        {
            return state.Length == 0 ? "SpaceNotch" : state;
        }

        return detail;
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
    /// <summary>
    /// Annonce une activité à Narrateur, une seule fois par activité et par
    /// titre : un téléchargement qui progresse ne parle pas à chaque bloc, mais
    /// « Téléchargé » est dit. Un appel interrompt la lecture ; le reste attend.
    /// </summary>
    private void Announce(IslandActivity? activity)
    {
        string key = activity is null ? string.Empty : $"{activity.Id}\u001F{activity.Title}";
        bool isNew = !string.Equals(key, _announcedKey, StringComparison.Ordinal);

        _announcedKey = key;

        if (Announcement.For(activity, isNew) is not { } announcement)
        {
            return;
        }

        try
        {
            AnnouncerText.Text = announcement.Text;
            AutomationProperties.SetName(IslandBody, announcement.Text);

            AutomationPeer? peer = FrameworkElementAutomationPeer.FromElement(AnnouncerText)
                ?? FrameworkElementAutomationPeer.CreatePeerForElement(AnnouncerText);

            peer?.RaiseNotificationEvent(
                AutomationNotificationKind.Other,
                announcement.Assertive
                    ? AutomationNotificationProcessing.ImportantAll
                    : AutomationNotificationProcessing.ImportantMostRecent,
                announcement.Text,
                "SpaceNotch.Activity");
        }
        catch (Exception ex)
        {
            // Aucun lecteur d'écran ne doit pouvoir faire tomber le rendu.
            MiniLogger.Log("[A11Y] annonce impossible", ex);
        }
    }

    /// <summary>
    /// Activités vraiment cachées : ni la présentée, ni celle de la bulle, ni
    /// celle que la notch retrouvera sous un retour temporaire.
    /// </summary>
    private int HiddenActivityCount() => SplitPresentation.HiddenCount(
        _controller.PresentedActivity,
        _activityManager.GetActiveActivities(),
        _settings.ShowSplitBubble ? SplitPresentation.BubbleFor(_controller.PresentedActivity, _activityManager.GetActiveActivities()) : null);

    private bool StackIndicatorVisible() => _settings.ShowActivityStack && HiddenActivityCount() > 0;

    private void UpdateStackIndicator()
    {
        int count = HiddenActivityCount() + 1;

        // Une seule notch : la pile se signale dans la notch elle-même, jamais
        // par un second objet posé à côté. Voir ADR-017.
        bool visible = StackIndicatorVisible();

        // Un nombre plutôt que des points : « +2 » se lit d'un coup d'œil, là
        // où « • • • » demandait de compter.
        string text = visible ? CompactTrailing.StackBadge(count) ?? string.Empty : string.Empty;
        Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;

        // Le compteur roule quand la pile grandit ou se vide.
        bool rolls = visible && !string.Equals(SignalStackIndicator.Text, text, StringComparison.Ordinal);

        SignalStackIndicator.Text = text;
        SignalStackIndicator.Visibility = state;
        CardStackIndicator.Text = text;
        CardStackIndicator.Visibility = state;

        if (rolls)
        {
            ContentTransition.Roll(SignalRestView.Visibility == Visibility.Visible ? SignalStackIndicator : CardStackIndicator, UseSpringAnimations());
        }
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

        // Vague 5, choix E1 : rien autour de la notch. Ni liseré ni halo ; la
        // couleur de l'activité vit seulement dans le contenu (icône, anneau,
        // pochette). La teinte reste calculée pour ce contenu et pour la pulsation.
        _atmosphere.SetGlowIntensity(0, 0);
        _atmosphere.SetGlowColor(Color.FromArgb(0xFF, ambient.Tint.R, ambient.Tint.G, ambient.Tint.B));

        // La dissolution respire avec la matière qui travaille : même fonction,
        // même période. Elle n'est relancée qu'au changement de mouvement, sans
        // quoi chaque rendu la ferait repartir de son image de départ.
        HypnoticPreset preset = activity is null || !AnimateHypnotic() || ambient.Pulse <= 0 || HypnoticResting()
            ? HypnoticPreset.None
            : HypnoticField.Resolve(activity.MotionState, activity.MotionPreset);

        if (preset != _atmospherePreset)
        {
            _atmospherePreset = preset;
            _atmosphere.SetHypnoticPulse(preset, ambient.Pulse);
            _atmosphere.SetBinaryRain(_settings.ShowBinaryRain && preset == HypnoticPreset.Process);
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

        // La couleur de l'activité (pochette, logo — D2) colore l'icône et
        // l'élément vivant ; sinon, celle de l'état. Jamais en contraste élevé.
        Brush tint = !_visualState.HighContrast && activity is not null && DeclaredTint(activity) is { } declared
            ? new SolidColorBrush(declared)
            : StatePalette.Brush(_visualState.HighContrast ? IslandActivityState.Idle : state);

        IdleStatusDot.Fill = tint;
        SignalGlyph.Tint = tint;
        CardGlyph.Tint = tint;
        SignalTrailing.Tint = tint;
        CardTrailing.Tint = tint;
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

        Geometry? silhouette = DeformedSilhouette(footprint) ?? _shape.Build(footprint, radius, geometry.Smoothing, shoulder: shoulder);
        RememberOutline(() => geometry.Silhouette(footprint));

        // Une géométrie nulle signifie « identique à la précédente » : le tracé
        // déjà posé est conservé, ce qui évite une reconstruction par image
        // quand le ressort ne bouge plus.
        if (silhouette is not null)
        {
            SurfaceFill.Data = silhouette;
        }

        SceneTrame.Resize(footprint.Width, footprint.Height, radius, shoulder);
        UpdateFocusTrace(footprint);

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
        // « Pas proche » plutôt que « écart > 0,25 » : au départ la valeur est NaN,
        // et toute comparaison avec NaN est fausse ; la marge ne se posait jamais.
        if (!(Math.Abs(shoulder - _contentShoulder) <= 0.25))
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
        => UsesSideTab
            ? SideTab.Preview(_restFootprint)
            : RestWeatherPreview() ?? IslandFootprint.PreviewOf(_tier, _restFootprint);

    /// <summary>Glyphe du palier signal, en DIPs (jeton NfSignalGlyphSize), et son écart au libellé.</summary>
    private const double SignalGlyphSpan = 14 + 8;

    /// <summary>Glyphe du palier carte, en DIPs (jeton NfCardGlyphSize), et son écart aux lignes.</summary>
    private const double CardGlyphSpan = 20 + 10;

    /// <summary>
    /// Forme au repos ajustée à ce qu'elle porte. Les textes sont mesurés hors
    /// de l'arbre visuel, avec les styles des textes affichés : la largeur est
    /// connue avant d'afficher quoi que ce soit, et le ressort l'anime comme
    /// n'importe quel autre changement de forme.
    /// </summary>
    private IslandFootprint FitRest(IslandActivity? activity, IslandPresentationTier tier)
        => UsesSideTab
            ? SideTab.Rest(_settings.TabSize, _settings.SideShoulderRadius)
            : FitRestFor(activity, tier);

    /// <summary>Forme au repos de la notch du haut — et de la pastille — ajustée à son contenu.</summary>
    private IslandFootprint FitRestFor(IslandActivity? activity, IslandPresentationTier tier)
    {
        if (activity is null || tier == IslandPresentationTier.Idle)
        {
            return IslandFootprint.For(tier, _settings.Density);
        }

        double stack = 0;

        if (StackIndicatorVisible())
        {
            _measureStack.Style ??= SignalStackIndicator.Style;
            stack += 6 + Measure(_measureStack, CompactTrailing.StackBadge(HiddenActivityCount() + 1));
        }

        CompactTrailing trailing = CompactTrailing.For(activity);

        if (CompactTrailing.MetricFor(activity, trailing) is { } metric)
        {
            _measureMetric.Style ??= SignalMetric.Style;
            stack += 8 + Measure(_measureMetric, metric);
        }

        // Le fil de niveau n'existe que dans la pastille ; les formes carrées,
        // dans les deux paliers.
        if (trailing.Kind != TrailingKind.None && (trailing.Kind != TrailingKind.Level || tier == IslandPresentationTier.Signal))
        {
            stack += trailing.Width + 6;
        }

        double content = tier == IslandPresentationTier.Signal
            ? SignalGlyphSpan + Measure(_measureSignal, activity.Title)
            : CardGlyphSpan + Math.Max(
                Measure(_measureSubhead, SubheadFor(activity)),
                Measure(_measureHeadline, activity.Title));

        double slack = string.Equals(_fitSlackFor, activity.Id, StringComparison.Ordinal) ? _fitSlack : 0;

        return IslandFootprint.Fit(tier, content + stack + slack, _settings.Geometry.Shoulder, _settings.Density);
    }

    /// <summary>
    /// Filet de sécurité de la largeur au repos : un texte coupé alors que la
    /// pastille pouvait encore grandir élargit la forme d'exactement ce qui
    /// manque. La mesure hors arbre et le rendu peuvent différer de quelques
    /// DIPs (police, mise à l'échelle du texte) ; « Volu… » ne doit jamais
    /// s'afficher quand « Volume » tenait.
    /// </summary>
    private void CatchUpTrimmed(TextBlock label)
    {
        // Pendant que le ressort bouge, un texte coupé l'est en passant : on
        // attend la forme posée (AnimationCompleted relance la vérification).
        FrameworkElement view = ReferenceEquals(label, SignalLabel) ? SignalRestView : CardRestView;

        if (!label.IsTextTrimmed
            || view.Visibility != Visibility.Visible
            || label.ActualWidth <= 0
            || _controller.IsAnimating
            || _controller.PresentedActivity is not { } activity
            || _controller.State is not (IslandState.Closed or IslandState.Preview)
            || _restFootprint.Width >= IslandFootprint.MaximumWidth(_tier) - 0.5)
        {
            return;
        }

        var probe = new TextBlock { Style = label.Style, Text = label.Text };
        probe.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        double missing = Math.Ceiling(probe.DesiredSize.Width - label.ActualWidth) + 1;

        if (missing <= 0)
        {
            return;
        }

        if (!string.Equals(_fitSlackFor, activity.Id, StringComparison.Ordinal))
        {
            _fitSlackFor = activity.Id;
            _fitSlack = 0;
        }

        _fitSlack += missing;
        MiniLogger.Log($"[FIT] texte coupé, la forme s'élargit de {missing} DIP ({label.Name})");
        RequestRender();
    }

    private static double Measure(TextBlock text, string? value)
    {
        text.Text = value ?? string.Empty;
        text.Measure(new global::Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));

        return text.DesiredSize.Width;
    }

    /// <summary>Ligne de contexte de la carte : celle que l'activité déclare, sinon celle qui se déduit.</summary>
    private static string SubheadFor(IslandActivity activity)
        => string.IsNullOrWhiteSpace(activity.Eyebrow) ? BuildSubhead(activity) : activity.Eyebrow;

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
        // Arrachée au bord, la notch suit sa propre géométrie : voir
        // IslandWindow.Detach. Tirée vers le bas, elle s'allonge en résistant.
        if (UsesFloatingGeometry)
        {
            ApplyFloatingGeometry(footprint);
            return;
        }

        if (UsesSideTab)
        {
            ApplySideGeometry(footprint);
            return;
        }

        if (_dragPhase is DragPhase.Pulling or DragPhase.Unpulling)
        {
            footprint = Detachment.Pulled(footprint, _pull);
        }

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
        //
        // L'Island qui change de place change aussi ce qu'elle recouvre : la
        // présence plein écran est relue, mais seulement quand le rectangle a
        // réellement bougé, et jamais à chaque image.
        MoveWindow(x, y, widthPx, heightPx, recheck: true);

        // Le corps XAML, lui, suit chaque image : c'est lui qui porte le morphing
        // sous-pixel, et il ne coûte qu'une passe de disposition sur un arbre
        // minuscule — sans aucun aller-retour avec le gestionnaire de fenêtres.
        IslandBody.Width = footprint.Width;
        IslandBody.Height = footprint.Height;

        ApplyShape(footprint);

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

        PlaceBubbleAttached(
            display,
            new ScreenRect((x - display.Left) / scale, 0, footprint.Width, footprint.Height),
            footprint);
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
        // Pendant un glisser et tant que la notch flotte, le moniteur est figé :
        // en mode « écran du curseur », le suivre ferait sauter la pastille d'un
        // écran à l'autre sous la main.
        if (_detachDisplay is not null)
        {
            return _detachDisplay;
        }

        switch (_settings.DisplayMode)
        {
            case IslandDisplayMode.Current:
                return DisplayManager.ResolveFromCursor();

            // L'écran où la notch a été accrochée par glisser, s'il est toujours
            // branché ; sinon l'écran principal.
            case IslandDisplayMode.Custom when FindDisplayByBounds(_settings.DockDisplayBounds) is { } docked:
                return docked;

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
        if (e.Message.MessageId == SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.WmHotkey
            && (int)e.Message.WParam == SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.LauncherId)
        {
            ToggleLauncherFromHotkey();
            e.Handled = true;
            return;
        }

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
            _islandShown = true;
            ApplyGeometry(_controller.CurrentFootprint);
            _atmosphere.SetVisible(true);
            _atmosphere.PlaceBehind(_hWnd);
            _appWindow.Show(activateWindow: false);
            UpdateBubble();
            return;
        }

        _islandShown = false;
        _atmosphere.SetVisible(false);
        _appWindow.Hide();
        UpdateBubble();
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
        // Un écran ajouté, retiré ou redimensionné rend la position d'une notch
        // détachée incertaine : elle revient au bord, sa place de référence.
        // L'écran d'accroche est recherché à nouveau.
        _dockDisplayCache = null;
        ForceAttach();

        SystemVisualState updated = SystemVisualState.Read();
        SpaceNotch_App.UI.MotionSettings.Invalidate();

        if (updated != _visualState)
        {
            _visualState = updated;
            GlyphView.AnimationsEnabled = updated.UseSpringAnimations;
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
    /// <summary>
    /// Activité ouverte par l'utilisateur, épargnée par l'expiration : une
    /// notification ouverte ne laisse pas place à la musique pendant qu'on la lit.
    /// </summary>
    private string? OpenedActivityId()
        => _controller.State is IslandState.Expanding or IslandState.Expanded ? _controller.PresentedActivity?.Id : null;

    private DispatcherQueueTimer? _clockTimer;

    /// <summary>
    /// Couleur propre d'une activité (D2) : celle qu'elle déclare (la pochette
    /// d'un morceau), sinon celle du logo de l'application qui notifie, calculée
    /// une fois puis gardée. <c>null</c> : la couleur de l'état fera l'affaire.
    /// </summary>
    private Color? DeclaredTint(IslandActivity activity)
    {
        if (activity.Tint is { } declared)
        {
            return Color.FromArgb(0xFF, declared.R, declared.G, declared.B);
        }

        if (activity.State == IslandActivityState.Notification
            && ArtworkTint.Get(activity.Artwork, () => OnUiThread(RequestRender)) is { } logo)
        {
            return Color.FromArgb(0xFF, logo.R, logo.G, logo.B);
        }

        return null;
    }

    /// <summary>
    /// Onglets glissants (U2) : notch ouverte et au moins deux activités qui
    /// durent — pas un retour de volume, pas une notification qui passe —, une
    /// rangée d'onglets s'ajoute en haut, dans l'ordre d'arrivée.
    /// </summary>
    private void UpdateTabs(IslandActivity? activity, bool expanded)
    {
        static bool Tabbable(IslandActivity a) => a.SceneKey is not (IslandSceneCatalog.QuickMenu or IslandSceneCatalog.Launcher or IslandSceneCatalog.Welcome);

        // Quatre onglets au plus, dont toujours celui de l'activité ouverte :
        // les autres, les plus récents, gardent leur ordre d'arrivée.
        List<IslandActivity> tabs = expanded && activity is not null && Tabbable(activity) && !UsesSideTab
            ? _activityManager.GetActiveActivities()
                .Where(a => Tabbable(a) && a.Id != activity.Id && ActivityPolicies.Resolve(a) != ActivityPresentationPolicy.Temporary)
                .OrderByDescending(a => a.CreatedAt)
                .Take(3)
                .Append(activity)
                .OrderBy(a => a.CreatedAt)
                .ToList()
            : [];

        if (tabs.Count < 2)
        {
            tabs.Clear();
        }

        double row = tabs.Count >= 2 ? TabStripView.RowHeight : 0;
        _controller.TabRowHeight = row;
        ContentArea.Padding = new Thickness(0, row, 0, 0);
        SceneTabs.Animate = UseSpringAnimations();
        SceneTabs.Show(tabs, activity?.Id, (Brush)Application.Current.Resources["NfTextPrimaryBrush"], (Brush)Application.Current.Resources["NfTextTertiaryBrush"]);
    }

    /// <summary>Fondu en pixels (A2) sur la forme d'arrivée ; faux s'il ne peut pas jouer.</summary>
    private bool PlayDissolve(IslandFootprint target)
    {
        if (!UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab || SurfaceFill.Fill is not Brush surface)
        {
            return false;
        }

        ShapePoint[] outline = _settings.Geometry.Silhouette(target);
        DissolveOverlay.Width = target.Width;
        DissolveOverlay.Height = target.Height;
        DissolveOverlay.Play(outline, target.Width, target.Height, surface);
        return true;
    }
    private IslandActivity? _lastRenderedActivity;

    /// <summary>
    /// Rayons de réussite (M4) : une seule fois, au moment où une activité
    /// passe à « terminé ». Jamais quand Windows réduit les animations.
    /// </summary>
    private void Celebrate(IslandActivity? activity)
    {
        IslandActivity? before = _lastRenderedActivity;
        _lastRenderedActivity = activity;

        if (activity is not null
            && activity.SceneKey is IslandSceneCatalog.Timer or IslandSceneCatalog.Pomodoro
            && SpaceNotch.Core.Motion.LightRays.Celebrates(before, activity))
        {
            PlayCue(SpaceNotch.Core.Sound.SoundCueKind.TimerDone);
        }

        if (activity is null || !SpaceNotch.Core.Motion.LightRays.Celebrates(before, activity) || !UseSpringAnimations() || UsesFloatingGeometry || UsesSideTab)
        {
            return;
        }

        Color tint = DeclaredTint(activity) ?? StatePalette.Tint(activity.State);

        // Les rayons partent sous la forme où l'activité se pose.
        IslandFootprint shape = _controller.State is IslandState.Expanded or IslandState.Expanding
            ? _controller.Opened(activity)
            : _restFootprint;

        _atmosphere.PlayLightRays(tint, shape.Width, shape.Height);
    }

    /// <summary>
    /// L'horloge au repos se met à jour à la minute exacte, et seulement quand
    /// elle est affichée : aucun minuteur ne tourne pour une horloge cachée.
    /// </summary>
    private void ArmClockTick(bool visible)
    {
        if (!visible)
        {
            _clockTimer?.Stop();
            return;
        }

        DateTime now = DateTime.Now;
        TimeSpan untilNextMinute = TimeSpan.FromSeconds(60 - now.Second) - TimeSpan.FromMilliseconds(now.Millisecond) + TimeSpan.FromMilliseconds(20);

        _clockTimer ??= CreateOneShotTimer(untilNextMinute, RequestRender);
        _clockTimer.Interval = untilNextMinute;
        _clockTimer.Stop();
        _clockTimer.Start();
    }

    private void RearmExpirationTimer()
    {
        TimeSpan? delay = _activityManager.GetTimeUntilNextExpiration(DateTimeOffset.UtcNow, OpenedActivityId());

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
        int expired = _activityManager.ExpireOverdue(DateTimeOffset.UtcNow, OpenedActivityId());

        if (expired > 0)
        {
            MiniLogger.Log($"Expiration : {expired} activité(s) retirée(s)");
        }

        RearmExpirationTimer();
    }

    private DispatcherQueueTimer CreateOneShotTimer(TimeSpan interval, Action onTick)
    {
        DispatcherQueueTimer timer = TrackTimer(_dispatcherQueue.CreateTimer());
        timer.IsRepeating = false;
        timer.Interval = interval;
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (!_isClosed)
            {
                onTick();
            }
        };

        return timer;
    }

    private DispatcherQueueTimer TrackTimer(DispatcherQueueTimer timer)
    {
        _ownedTimers.Add(timer);
        return timer;
    }

    private void StopOwnedTimers()
    {
        foreach (DispatcherQueueTimer timer in _ownedTimers)
        {
            timer.Stop();
        }

        _ownedTimers.Clear();
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
        SurfaceFill.Fill = CreateSurfaceBrush(light, mode == IslandBackdropMode.Opaque);

        // Contour optionnel, pour les fonds d'écran sombres où le noir se perd.
        SurfaceFill.Stroke = CreateOutlineBrush(light);
        SurfaceFill.StrokeThickness = SurfaceFill.Stroke is null ? 0 : 1;

        // La goutte est la même matière que la notch ; la bulle aussi, dans sa
        // propre fenêtre — d'où des pinceaux à elle, de même teinte.
        ApplyVeilBrush();
        GooFill.Fill = SurfaceFill.Fill;
        GooFill.Stroke = SurfaceFill.Stroke;
        GooFill.StrokeThickness = SurfaceFill.StrokeThickness;
        _bubble.SetSurface(CreateSurfaceBrush(light, mode == IslandBackdropMode.Opaque));
        _bubble.SetOutline(CreateOutlineBrush(light), SurfaceFill.StrokeThickness);

        _atmosphere.SetShadowStrength(_settings.FloatingShadowOpacity);

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

    /// <summary>
    /// Surface de la notch : la teinte choisie — noir OLED par défaut,
    /// graphite, ou une couleur libre — et sa transparence. Le thème clair
    /// garde sa surface claire ; seule la transparence s'y applique.
    /// </summary>
    private SolidColorBrush CreateSurfaceBrush(bool light, bool opaque)
    {
        (byte alpha, byte r, byte g, byte b) = _settings.SurfaceColor();

        if (light)
        {
            var baseBrush = (SolidColorBrush)AtmosphericMaskHelper.CreateSolidSurface(light: true, opaque: opaque);
            Color c = baseBrush.Color;
            return new SolidColorBrush(Color.FromArgb((byte)Math.Min(c.A, alpha), c.R, c.G, c.B));
        }

        return new SolidColorBrush(Color.FromArgb(alpha, r, g, b));
    }

    /// <summary>Contour de la notch, s'il est demandé : un filet d'encre à peine visible.</summary>
    private SolidColorBrush? CreateOutlineBrush(bool light)
    {
        if (!_settings.ShowOutline)
        {
            return null;
        }

        byte alpha = (byte)Math.Round(Math.Clamp(_settings.OutlineOpacity, 0.05, 0.5) * 255);
        return new SolidColorBrush(light ? Color.FromArgb(alpha, 0, 0, 0) : Color.FromArgb(alpha, 255, 255, 255));
    }

    // ------------------------------------------------------------------
    // Interactions
    // ------------------------------------------------------------------

    private void OnIslandPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _pixelHovered = true;

        // Une entrée annule la fermeture en attente : le pointeur qui revient
        // dans le délai de grâce retrouve l'aperçu au lieu de le faire renaître.
        _previewExitTimer?.Stop();

        // La main qui tient la notch passe sans cesse sur elle : ce n'est pas un
        // survol, et la forme ne doit pas changer sous elle.
        if (_dragPhase != DragPhase.None)
        {
            return;
        }

        // Survol prolongé : environ une seconde de pose ouvre la notch, si
        // l'utilisateur l'a choisi. La pose courte reste un simple aperçu.
        if (_settings.HoverToExpand && _controller.PresentedActivity is not null)
        {
            _hoverExpandTimer ??= CreateOneShotTimer(HoverExpandDwell, OnHoverExpandTick);
            _hoverExpandTimer.Stop();
            _hoverExpandTimer.Start();
        }

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

        // Le survol ne capture ni focus ni clavier : l'utilisateur peut traverser
        // la zone sans interrompre la saisie dans l'application active. La
        // capture est réservée aux commandes explicites qui ouvrent une saisie.
    }

    /// <summary>Vrai quand une zone de texte de la notch a le focus : on y tape.</summary>
    private bool IsTyping()
        => Content?.XamlRoot is { } root
            && FocusManager.GetFocusedElement(root) is TextBox;

    /// <summary>
    /// La fenêtre devient activable et prend le premier plan, le temps de
    /// taper. Elle redevient inactivable quand la notch se referme.
    /// </summary>
    private void CaptureKeyboardForTyping()
    {
        WindowChrome.SetKeyboardCapture(_hWnd, enabled: true);
        WindowChrome.BringToForeground(_hWnd);
        _typingCapture = true;
    }

    private bool _typingCapture;

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
        _pixelHovered = false;
        SceneTrame.Spotlight(null);

        // Un passage trop bref n'a jamais été une intention : l'aperçu n'a pas
        // lieu du tout.
        _previewEnterTimer?.Stop();
        _hoverExpandTimer?.Stop();

        if (_dragPhase != DragPhase.None)
        {
            return;
        }

        _previewExitTimer ??= CreateOneShotTimer(PreviewExitGrace, OnPreviewExitTick);

        _previewExitTimer.Stop();
        _previewExitTimer.Start();

        // Pendant la frappe, le clavier reste à la notch même si la souris
        // s'en va : il ne lui est rendu qu'à la fermeture.
        if (!_typingCapture && !IsTyping())
        {
            WindowChrome.SetKeyboardCapture(_hWnd, enabled: false);
        }
    }

    private void OnPreviewExitTick()
    {
        // La notch flottante qu'on vient de lâcher, ou la bulle que le pointeur
        // vise : l'aperçu reste le temps du geste.
        if (_dragPhase != DragPhase.None)
        {
            return;
        }

        _controller.EndPreview();
    }

    private void OnHoverExpandTick()
    {
        if (_dragPhase != DragPhase.None || _controller.State is not (IslandState.Closed or IslandState.Preview))
        {
            return;
        }

        RevealPresented();
    }

    /// <summary>
    /// Pose avant qu'un survol ouvre la notch, quand l'option est active. Une
    /// seconde : au-delà des 0,3 à 0,5 s de l'intention, pour qu'un pointeur
    /// qui ne fait que s'attarder n'ouvre rien.
    /// </summary>
    private static readonly TimeSpan HoverExpandDwell = TimeSpan.FromSeconds(1);

    private DispatcherQueueTimer? _hoverExpandTimer;

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

        _hoverExpandTimer?.Stop();

        PointerPointProperties properties = e.GetCurrentPoint(IslandBody).Properties;

        if (properties.IsRightButtonPressed)
        {
            e.Handled = true;
            ToggleQuickMenu();
            return;
        }

        // Double-clic (F7) : le second clic arrive pendant que la notch s'ouvre ;
        // au lieu de la refermer, il ouvre la note. Le premier clic n'attend rien.
        if (properties.IsLeftButtonPressed
            && Environment.TickCount64 - _lastClickAt < (long)DoubleClickDelay().TotalMilliseconds
            && !UsesFloatingGeometry)
        {
            e.Handled = true;
            _lastClickAt = 0;
            OpenNote();
            return;
        }

        // Clic du milieu (U3) : lecture ou pause, sans ouvrir la notch.
        if (properties.IsMiddleButtonPressed)
        {
            e.Handled = true;
            _ = SendMediaActionAsync(MediaFeature.PlayPauseAction);
            return;
        }

        // Forme compacte : l'appui ne décide encore rien. Relâché sur place,
        // c'est un clic ; tiré, c'est un glisser — l'arrachement au bord, ou le
        // déplacement d'une notch déjà détachée. Voir IslandWindow.Detach.
        if (properties.IsLeftButtonPressed
            && _controller.State is IslandState.Closed or IslandState.Preview
            && _dragPhase is DragPhase.None or DragPhase.Settling or DragPhase.Unpulling)
        {
            e.Handled = true;
            _pressOpensLauncher = _controller.PresentedActivity is null;
            BeginPress(e);
            return;
        }

        if (_controller.State is IslandState.Closed && _controller.PresentedActivity is null)
        {
            e.Handled = true;
            OpenLauncher();
            return;
        }

        _controller.ToggleFromUser();
    }

    /// <summary>Clic validé au relâcher, sur une forme compacte.</summary>
    private long _lastClickAt;

    private void CommitClick()
    {
        _lastClickAt = Environment.TickCount64;

        if (_pressOpensLauncher && _controller.PresentedActivity is null)
        {
            OpenLauncher();
            return;
        }

        bool opening = _controller.State is not (IslandState.Expanded or IslandState.Expanding);
        _controller.ToggleFromUser();

        if (opening && _controller.State is IslandState.Expanded or IslandState.Expanding)
        {
            PlayCue(SpaceNotch.Core.Sound.SoundCueKind.Open);
        }
    }

    /// <summary>
    /// Le raccourci global : ouvre la recherche, ou la referme si elle est
    /// déjà devant — le même geste dans les deux sens, comme Spotlight.
    /// </summary>
    private void ToggleLauncherFromHotkey()
    {
        if (_isClosed)
        {
            return;
        }

        if (_controller.State != IslandState.Closed
            && _controller.PresentedActivity?.SceneKey == IslandSceneCatalog.Launcher)
        {
            _controller.RequestCollapse();
            return;
        }

        OpenLauncher();
    }

    /// <summary>Note éclair (F7) : la note s'ouvre dans la notch, curseur à la fin.</summary>
    private void OpenNote()
    {
        _quickMenuFeature.Dismiss();
        _noteFeature.Show();
        _activityManager.PinPresentation(NoteFeature.ActivityId);
        RevealPresented();
    }

    /// <summary>Ouvre la grille de fonctions et la montre.</summary>
    private void OpenLauncher()
    {
        _launcherFeature.Show();
        RevealPresented();
    }

    /// <summary>L'appui en cours ouvrira la grille de fonctions s'il reste un clic : rien d'autre à déplier.</summary>
    private bool _pressOpensLauncher;

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
    /// <summary>
    /// Gestes sur la notch (U3) : la molette règle le volume (2 % par cran, le
    /// fader cranté s'affiche), un balayage horizontal change de morceau.
    /// Ctrl + molette parcourt toujours la pile d'activités.
    /// </summary>
    private void OnIslandPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPointProperties properties = e.GetCurrentPoint(IslandBody).Properties;
        int delta = properties.MouseWheelDelta;

        if (delta == 0)
        {
            return;
        }

        bool ctrl = (e.KeyModifiers & global::Windows.System.VirtualKeyModifiers.Control) != 0;

        if (properties.IsHorizontalMouseWheel)
        {
            // Pavé tactile : un balayage produit une rafale ; un seul morceau par geste.
            long now = Environment.TickCount64;

            if (now - _lastSwipe > 450)
            {
                _lastSwipe = now;
                _ = SendMediaActionAsync(delta > 0 ? MediaFeature.NextAction : MediaFeature.PreviousAction);
            }

            e.Handled = true;
            return;
        }

        if (!ctrl && _volumeListener.Level is float level)
        {
            // Butée (A6) : pousser au-delà de 100 % ou sous zéro secoue la notch.
            if ((level >= 0.999f && delta > 0) || (level <= 0.001f && delta < 0))
            {
                BumpContent();
            }

            _volumeListener.SetLevel((float)VolumeFader.Wheel(level, delta / 120.0));
            _diagnostics.CountEvent();
            e.Handled = true;
            return;
        }

        if (_controller.CyclePresentation(delta > 0 ? 1 : -1))
        {
            _diagnostics.CountEvent();
            e.Handled = true;
        }
    }

    private long _lastSwipe;

    /// <summary>Envoie une commande au lecteur en cours, s'il y en a un.</summary>
    private async Task SendMediaActionAsync(string actionId)
    {
        IslandActivity? media = _activityManager.GetActiveActivities()
            .FirstOrDefault(a => string.Equals(a.FeatureId, MediaFeature.FeatureKey, StringComparison.Ordinal));

        if (media is null)
        {
            return;
        }

        try
        {
            await _featureRegistry.HandleActionAsync(new IslandActionRequest(media.Id, actionId));
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[GESTE] Commande du lecteur impossible", ex);
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
        // Une touche tapée dans un champ lui appartient : les flèches changeaient
        // d'activité, Espace lançait l'action principale. Seul Échap, que le
        // champ n'a pas traité, referme la notch.
        if (e.OriginalSource is TextBox && e.Key != global::Windows.System.VirtualKey.Escape)
        {
            return;
        }

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
                // Marqué traité avant d'attendre : après un await, il serait
                // trop tard pour arrêter la remontée de la touche.
                e.Handled = true;
                _diagnostics.CountEvent();
                await InvokePrimaryActionAsync();
                return;

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
        e.DragUIOverride.Caption = Lang.T("Déposer dans la notch", "Drop into the notch");

        // La goutte (P2) pend vers le fichier, et le suit.
        HangDrop(e.GetPosition(IslandBody).X);

        ShowDropTarget();
    }

    /// <summary>La notch devient une cible de dépôt. DragOver arrive en rafale : la mise en place n'a lieu qu'une fois.</summary>
    private void ShowDropTarget()
    {
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
        TabRestView.Visibility = Visibility.Collapsed;
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

        RetractDrop();
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
            PlayCue(SpaceNotch.Core.Sound.SoundCueKind.Drop);
            SwallowDrop();

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

            // WinUIEx impose par défaut une taille minimale de 136 × 39 DIP. La
            // notch au repos en fait 80 × 18 : Windows élargissait la fenêtre
            // au-delà du rectangle calculé, et la notch se retrouvait décentrée
            // par rapport à son halo, dans une fenêtre qui avalait les clics.
            windowManager.MinWidth = 0;
            windowManager.MinHeight = 0;
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
            Text = expanded ? Lang.T("Réduire la notch", "Collapse the notch") : Lang.T("Déployer la notch", "Expand the notch"),
            Icon = new FontIcon { Glyph = expanded ? "\uE70E" : "\uE70D" }
        };
        toggleItem.Click += (_, _) => _controller.ToggleFromUser();

        flyout.Items.Add(toggleItem);

        // Détachée, la notch se raccroche aussi d'ici : le geste de la ramener
        // au bord n'est pas le seul chemin.
        if (UsesFloatingGeometry)
        {
            var attachItem = new MenuFlyoutItem
            {
                Text = Lang.T("Raccrocher au bord de l’écran", "Dock to the screen edge"),
                Icon = new FontIcon { Glyph = "\uE8A7" }
            };
            attachItem.Click += (_, _) => ReattachFromMenu();
            flyout.Items.Add(attachItem);
        }

        flyout.Items.Add(BuildLaunchMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());
        flyout.Items.Add(BuildActivitiesMenu());
        flyout.Items.Add(BuildMotionMenu());
        flyout.Items.Add(BuildAppearanceMenu());
        flyout.Items.Add(new MenuFlyoutSeparator());

        var settingsItem = new MenuFlyoutItem
        {
            Text = Lang.T("Réglages…", "Settings…"),
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

        var exitItem = new MenuFlyoutItem { Text = Lang.T("Quitter SpaceNotch", "Quit SpaceNotch") };
        exitItem.Click += (_, _) => QuitApplication();
        flyout.Items.Add(exitItem);

        return flyout;
    }

    /// <summary>Ce qu'on lance depuis la notch : applications, minuteurs, focus.</summary>
    private MenuFlyoutSubItem BuildLaunchMenu()
    {
        var menu = new MenuFlyoutSubItem
        {
            Text = Lang.T("Lancer", "Start"),
            Icon = new FontIcon { Glyph = "\uE768" }
        };

        var launcherItem = new MenuFlyoutItem { Text = Lang.T("Rechercher…", "Search…") };
        launcherItem.Click += (_, _) =>
        {
            _launcherFeature.Show();
            RevealPresented();
        };

        bool countdown = _timerFeature.IsMeasuring && _timerFeature.Mode is TimerMode.Countdown;
        var timerItem = new MenuFlyoutItem { Text = countdown ? Lang.T("Arrêter le minuteur", "Stop timer") : Lang.T("Minuteur (5 min)", "Timer (5 min)") };
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
        var stopwatchItem = new MenuFlyoutItem { Text = stopwatch ? Lang.T("Arrêter le chronomètre", "Stop stopwatch") : Lang.T("Chronomètre", "Stopwatch") };
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
            Text = _pomodoroFeature.IsSessionRunning ? Lang.T("Mettre le focus en pause", "Pause focus") : "Focus (25 min)"
        };
        focusItem.Click += (_, _) =>
        {
            if (_pomodoroFeature.IsSessionRunning)
            {
                _pomodoroFeature.Pause();
            }
            else
            {
                // Focus calé sur l'agenda (W3) : il finit avant la prochaine réunion.
                _pomodoroFeature.StartFitted();
            }

            RevealPresented();
        };

        // Capture de texte (W4) : aussi « ocr » ou « texte » dans la recherche.
        var captureItem = new MenuFlyoutItem { Text = Lang.T("Capturer du texte", "Capture text") };
        captureItem.Click += (_, _) => StartTextCapture();

        var demoItem = new MenuFlyoutItem { Text = Lang.T("Démonstration", "Demo") };
        demoItem.Click += (_, _) => StartDemo();

        menu.Items.Add(launcherItem);
        menu.Items.Add(demoItem);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(timerItem);
        menu.Items.Add(stopwatchItem);
        menu.Items.Add(focusItem);
        menu.Items.Add(captureItem);

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
            Text = Lang.T("Activités", "Activities"),
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
            Text = Lang.T("Mouvement", "Motion"),
            Icon = new FontIcon { Glyph = "\uE916" }
        };

        foreach ((MotionStyle style, string label) in new[]
                 {
                     (MotionStyle.Quiet, Lang.T("Calme", "Calm")),
                     (MotionStyle.Natural, Lang.T("Naturel", "Natural")),
                     (MotionStyle.Dynamic, Lang.T("Dynamique", "Lively"))
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
            Text = Lang.T("Mouvement hypnotique", "Hypnotic motion"),
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
            Text = Lang.T("Apparence", "Appearance"),
            Icon = new FontIcon { Glyph = "\uE790" }
        };

        foreach ((IslandAppearance appearance, string label) in new[]
                 {
                     (IslandAppearance.Dark, Lang.T("Sombre", "Dark")),
                     (IslandAppearance.Light, Lang.T("Clair", "Light")),
                     (IslandAppearance.Auto, Lang.T("Automatique", "Automatic"))
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
        bool displayChanged = settings.DisplayMode != _settings.DisplayMode
            || settings.CustomDisplayHandle != _settings.CustomDisplayHandle;

        _settings = settings;
        _clipboardFeature.IgnoreSecrets = settings.ClipboardIgnoreSecrets;
        _weatherFeature.SetCity(settings.WeatherCity);
        _notificationFeature.IgnoredApps = settings.IgnoredNotificationApps;
        _launcherFeature.WebSearchEngine = settings.WebSearchEngine;

        // Écran de veille (P5) : la vérification d'inactivité ne tourne que s'il est voulu.
        ArmScreensaver();

        // Le détachement retiré, ou l'écran cible changé : la notch revient au
        // bord de l'écran qui est désormais le sien.
        if (!_settings.AllowDetach || displayChanged)
        {
            ForceAttach();
        }

        // Le bord choisi dans les réglages — ou les côtés désactivés — : la
        // notch accrochée change de bord sans passer par un geste.
        if (!UsesFloatingGeometry)
        {
            NotchEdge previous = _edge;
            LoadDock();

            if (previous != _edge)
            {
                ApplyLayout();
            }
        }

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
        _restFootprint = FitRest(_controller.PresentedActivity, _tier);
        _controller.UpdateCollapsedFootprint(_restFootprint);
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

    /// <summary>
    /// Un second lancement — raccourci, menu Démarrer, installeur — ne crée pas
    /// une seconde notch : il réveille celle-ci, qui se montre et s'ouvre.
    /// </summary>
    public void RevealFromSecondLaunch()
    {
        MiniLogger.Log("Second lancement : la notch en cours se montre.");

        if (!_islandShown)
        {
            SetIslandVisible(true);
        }

        if (_controller.State is not (IslandState.Expanded or IslandState.Expanding))
        {
            _controller.ToggleFromUser();
        }
    }

    /// <summary>
    /// Rejoue le scénario de démonstration dans la vraie notch : chaque étape est
    /// publiée dans le vrai gestionnaire d'activités, à son heure, par un unique
    /// minuteur à usage unique réarmé d'étape en étape.
    /// </summary>
    public void StartDemo()
    {
        _demoSteps = DemoScenario.Steps();
        _demoIndex = 0;
        _demoStart = DateTimeOffset.UtcNow;

        MiniLogger.Log($"[DEMO] scénario lancé : {_demoSteps.Count} étapes");

        ScheduleNextDemoStep();
    }

    private IReadOnlyList<DemoStep> _demoSteps = [];
    private int _demoIndex;
    private DateTimeOffset _demoStart;
    private DispatcherQueueTimer? _demoTimer;

    private void ScheduleNextDemoStep()
    {
        if (_demoIndex >= _demoSteps.Count)
        {
            MiniLogger.Log("[DEMO] scénario terminé");
            return;
        }

        TimeSpan due = _demoSteps[_demoIndex].At - (DateTimeOffset.UtcNow - _demoStart);

        _demoTimer ??= CreateOneShotTimer(TimeSpan.FromMilliseconds(50), PlayDemoStep);
        _demoTimer.Interval = due > TimeSpan.FromMilliseconds(10) ? due : TimeSpan.FromMilliseconds(10);
        _demoTimer.Stop();
        _demoTimer.Start();
    }

    private void PlayDemoStep()
    {
        DemoStep step = _demoSteps[_demoIndex++];

        if (step.Post?.Invoke(DateTimeOffset.UtcNow) is { } activity)
        {
            _activityManager.PostActivity(activity);
        }

        if (step.RemoveId is { } id)
        {
            _activityManager.RemoveActivity(id);
        }

        RearmExpirationTimer();
        ScheduleNextDemoStep();
    }

    private void OpenSettingsWindow()
    {
        _dispatcherQueue.TryEnqueue(() =>
        {
            try
            {
                if (_settingsWindow is null)
                {
                    _settingsWindow = new SettingsWindow(_settingsService, _featureRegistry)
                    {
                        ReplayWelcome = ShowWelcome,
                        RequestNotificationAccess = _notificationFeature.RequestAccessAsync,
                        SearchHotkey = _launcherFeature.Hotkey
                    };
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

    private async void OnWindowClosed(object sender, WindowEventArgs args) => await ShutdownAsync();

    /// <summary>
    /// « Quitter » : l'arrêt est mené jusqu'au bout — fonctionnalités arrêtées,
    /// raccourci rendu à Windows, journal vidé — <em>puis</em> l'application
    /// sort. <c>Application.Exit()</c> seul coupait le processus pendant que
    /// l'arrêt attendait encore, et laissait le raccourci global enregistré.
    /// </summary>
    private async void QuitApplication()
    {
        await ShutdownAsync();
        Application.Current.Exit();
    }

    private async Task ShutdownAsync()
    {
        if (_isClosed)
        {
            return;
        }

        _isClosed = true;
        StopOwnedTimers();
        _notificationPixelSubscription?.Dispose();
        _notificationPixelSubscription = null;
        _gazeTimer?.Stop();
        _blinkTimer?.Stop();

        _geometryTimer?.Stop();
        _expirationTimer?.Stop();
        _previewEnterTimer?.Stop();
        _previewExitTimer?.Stop();
        _attenuationTimer?.Stop();
        _demoTimer?.Stop();
        _hoverExpandTimer?.Stop();
        _bubbleRestTimer?.Stop();
        HookDetachFrames();

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

            _bubble.Close();

            _dropCompletionTimer?.Stop();
            _signalHypnotic?.Dispose();
            _cardHypnotic?.Dispose();
            _tabHypnotic?.Dispose();
            _dropHypnotic?.Dispose();

            _clipboardMonitor.Dispose();
            _bluetoothWatcher.Dispose();
            _volumeListener.Dispose();
            _brightnessService.Dispose();
            _mediaSessionManager.Shutdown();
            _controller.Dispose();
            _diagnostics.Dispose();
            _screenWatcher.Dispose();
            _notificationListener.Dispose();
            SpaceNotch.Platform.Windows.Launcher.GlobalHotkey.Unregister(_hWnd);
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
