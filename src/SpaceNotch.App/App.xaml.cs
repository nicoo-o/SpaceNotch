using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SpaceNotch.Core.Setup;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Win32;
using SpaceNotch_App.Setup;
using SpaceNotch_App.Startup;
using SpaceNotch_App.Windows;

namespace SpaceNotch_App;

/// <summary>
/// Point d'entrée de l'application.
///
/// Son rôle est volontairement réduit : démarrer le journal, créer l'Island,
/// éventuellement ouvrir les réglages si on le lui demande. Toute la logique vit
/// dans la fenêtre de l'Island et dans les fonctionnalités — un point d'entrée qui
/// accumule de la logique devient le fichier que plus personne n'ose modifier.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private SingleInstance? _instance;

    public App()
    {
        // Le produit est noir OLED : les menus, info-bulles et fenêtres de
        // l'application suivent le thème sombre, quel que soit celui de Windows.
        // Sans cela, un Windows en thème clair donnait des info-bulles et des
        // boutons clairs — le « flash blanc » au clic dans l'installeur.
        RequestedTheme = ApplicationTheme.Dark;

        InitializeComponent();

        // Le journal démarre avant tout le reste : un échec de démarrage doit
        // laisser une trace exploitable.
        MiniLogger.Start();

        UnhandledException += OnUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            MiniLogger.Log($"[FATAL] AppDomain.UnhandledException : {args.ExceptionObject}");

        // Les tâches lancées sans attente (« _ = … ») qui échouent : sans ce
        // gestionnaire, leur erreur disparaissait sans laisser de trace.
        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, args) =>
        {
            MiniLogger.Log($"[WARN] Tâche en échec non observée : {args.Exception}");
            args.SetObserved();
        };

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            MiniLogger.Log("Arrêt du processus.");
    }

#if DEBUG
    private static readonly List<DispatcherQueueTimer> FaultTimers = [];

    /// <summary>
    /// Preuve du n° 33 sur la vraie notch, en build Debug seulement
    /// (<c>--fault-test</c>) : une exception levée par un minuteur à 3 s, une autre
    /// après un <c>await</c> dans un gestionnaire <c>async void</c> à 5 s, puis
    /// une ligne de journal à 8 s si la notch tourne encore.
    /// </summary>
    private static async void ThrowAfterAwait()
    {
        await System.Threading.Tasks.Task.Yield();
        throw new InvalidOperationException("Essai n° 33 : exception d'un async void après un await");
    }

    private static void StartFaultTest(DispatcherQueue queue)
    {
        void After(double seconds, global::Windows.Foundation.TypedEventHandler<DispatcherQueueTimer, object> tick)
        {
            DispatcherQueueTimer timer = queue.CreateTimer();
            timer.Interval = TimeSpan.FromSeconds(seconds);
            timer.IsRepeating = false;
            timer.Tick += tick;
            FaultTimers.Add(timer);
            timer.Start();
        }

        After(3, SpaceNotch_App.Diagnostics.Guard.Tick((_, _) => throw new InvalidOperationException("Essai n° 33 : exception d'un minuteur")));
        After(5, (_, _) => queue.TryEnqueue(ThrowAfterAwait));
        After(6, (_, _) => queue.TryEnqueueSafely(() => throw new InvalidOperationException("Essai n° 33 : exception d'un travail posté")));
        After(8, (_, _) => MiniLogger.Log("[ESSAI] n° 33 : la notch tourne toujours après les trois exceptions"));
    }
#endif

    /// <summary>Vrai une fois la notch lancée : l'installeur a son propre gestionnaire, qui s'arrête.</summary>
    private bool _islandRunning;

    /// <summary>
    /// Une exception d'un gestionnaire XAML ou <c>async void</c> (les Réglages)
    /// fermait la notch (n° 33). Notch lancée, elle est journalisée et
    /// l'application continue ; avant, et dans l'installeur, rien ne change. Les
    /// minuteurs et le travail posté ne passent pas par ici : voir
    /// <see cref="SpaceNotch_App.Diagnostics.Guard"/>.
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs args)
    {
        if (!_islandRunning)
        {
            MiniLogger.Log($"[FATAL] App.UnhandledException : {args.Message} — {args.Exception}");
            return;
        }

        args.Handled = true;
        SpaceNotch_App.Diagnostics.Guard.Report("XAML", args.Exception);
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MiniLogger.Log("App.OnLaunched starting");

        // Le même exécutable est aussi son installeur : « SpaceNotch-Setup.exe »,
        // ou --install / --uninstall. Dans ce cas, pas d'Island — la notch de
        // l'installeur, ou rien du tout pour le travail élevé et les
        // installations scriptées. Voir ADR-022.
        SetupCommand setup = SetupCommand.Parse(Environment.GetCommandLineArgs(), Environment.ProcessPath);

        if (setup.Mode != SetupMode.None)
        {
            LaunchSetup(setup);
            return;
        }

        // Une seule notch par session : un second lancement réveille la
        // première et se retire.
        _instance = SingleInstance.TryClaim();

        if (_instance is null)
        {
            Exit();
            return;
        }

        try
        {
            var island = new IslandWindow();

            _window = island;
            _window.Closed += (_, _) => MiniLogger.Log("Fenêtre de l'Island fermée.");

            _window.Activate();
            _islandRunning = true;

            DispatcherQueue queue = island.DispatcherQueue;
            _instance.ListenForReveal(() => queue.TryEnqueueSafely(island.RevealFromSecondLaunch));

            MiniLogger.Log("App.OnLaunched completed and window activated");

            // Vérifié par le workflow Release après une installation : l'identité
            // de paquet doit être là, sans quoi les notifications restent muettes.
            MiniLogger.Log(SpaceNotch.Platform.Windows.Setup.IdentityPackage.HasIdentity
                ? "Identité de paquet : présente"
                : "Identité de paquet : absente");

            // Les options sont traitées après l'affichage : une commodité de
            // lancement ne doit jamais retarder l'Island, qui est la raison d'être
            // de l'application.
            CommandLineOptions options = CommandLineOptions.Parse(Environment.GetCommandLineArgs());

            if (options.StartedByWindows)
            {
                MiniLogger.Log("Lancement automatique au démarrage de Windows.");
            }

            if (options.MeasureFrames)
            {
                SpaceNotch_App.Animations.FrameClock.MeasureRuns(island.MotionContext);
            }

#if DEBUG
            if (Environment.GetCommandLineArgs().Contains("--fault-test", StringComparer.Ordinal))
            {
                StartFaultTest(queue);
            }
#endif

            if (options.OpenSettings)
            {
                island.ShowSettings();
            }

            if (options.RunDemo)
            {
                island.StartDemo();
            }
            else if (options.RunTour)
            {
                island.StartTour();
            }
            else
            {
                // Premier lancement : la notch se présente d'elle-même.
                island.OfferWelcome();
            }
        }
        catch (Exception ex)
        {
            // Jamais de processus fantôme, sans fenêtre ni icône : l'utilisateur
            // voit pourquoi, et le processus se retire.
            MiniLogger.Log($"[FATAL] Exception in OnLaunched: {ex}");
            NativeMethods.MessageBox(IntPtr.Zero, $"SpaceNotch n'a pas pu démarrer.\n\n{ex.Message}\n\n{MiniLogger.LogPath}", SetupIdentity.ProductName, 0x10);
            Environment.Exit(1);
        }
    }

    private void LaunchSetup(SetupCommand setup)
    {
        string version = typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

        if (setup.IsWorker || setup.Quiet)
        {
            _ = RunHeadlessAsync(setup, version);
            return;
        }

        // Une erreur de l'installeur ne doit jamais finir en lancement muet :
        // l'utilisateur voit au moins pourquoi, et le journal garde le détail.
        UnhandledException += (_, args) =>
        {
            args.Handled = true;
            FailSetup(args.Exception);
        };

        try
        {
            _window = new SetupWindow(setup, version);
            _window.Activate();
            MiniLogger.Log($"Installeur ouvert : {SetupWindow.Describe(setup)}");
        }
        catch (Exception ex)
        {
            FailSetup(ex);
        }
    }

    private static void FailSetup(Exception ex)
    {
        MiniLogger.Log($"[FATAL] Installeur : {ex}");

        const uint IconError = 0x10;
        SetupText text = SetupText.Current;
        NativeMethods.MessageBox(IntPtr.Zero, $"{text.Failed}\n\n{ex.Message}\n\n{MiniLogger.LogPath}", SetupIdentity.ProductName, IconError);

        Environment.Exit(SetupRunner.Failed);
    }

    private static async Task RunHeadlessAsync(SetupCommand setup, string version)
    {
        int code = await SetupRunner.RunAsync(setup, version).ConfigureAwait(false);
        Environment.Exit(code);
    }
}
