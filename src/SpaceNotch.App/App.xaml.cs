using Microsoft.UI.Xaml;
using SpaceNotch.Core.Setup;
using SpaceNotch.Infrastructure.Logging;
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

    public App()
    {
        InitializeComponent();

        // Le journal démarre avant tout le reste : un échec de démarrage doit
        // laisser une trace exploitable.
        MiniLogger.Start();

        UnhandledException += (sender, args) =>
            MiniLogger.Log($"[FATAL] App.UnhandledException : {args.Message} — {args.Exception}");

        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
            MiniLogger.Log($"[FATAL] AppDomain.UnhandledException : {args.ExceptionObject}");

        AppDomain.CurrentDomain.ProcessExit += (sender, args) =>
            MiniLogger.Log("Arrêt du processus.");
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

        try
        {
            var island = new IslandWindow();

            _window = island;
            _window.Closed += (_, _) => MiniLogger.Log("Fenêtre de l'Island fermée.");

            _window.Activate();

            MiniLogger.Log("App.OnLaunched completed and window activated");

            // Les options sont traitées après l'affichage : une commodité de
            // lancement ne doit jamais retarder l'Island, qui est la raison d'être
            // de l'application.
            CommandLineOptions options = CommandLineOptions.Parse(Environment.GetCommandLineArgs());

            if (options.StartedByWindows)
            {
                MiniLogger.Log("Lancement automatique au démarrage de Windows.");
            }

            if (options.OpenSettings)
            {
                island.ShowSettings();
            }

            if (options.RunDemo)
            {
                island.StartDemo();
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log($"[FATAL] Exception in OnLaunched: {ex}");
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

        try
        {
            _window = new SetupWindow(setup, version);
            _window.Activate();
        }
        catch (Exception ex)
        {
            MiniLogger.Log($"[FATAL] Installeur : {ex}");
            Exit();
        }
    }

    private static async Task RunHeadlessAsync(SetupCommand setup, string version)
    {
        int code = await SetupRunner.RunAsync(setup, version).ConfigureAwait(false);
        Environment.Exit(code);
    }
}
