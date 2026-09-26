using System;
using System.Threading.Tasks;
using SpaceNotch.Core.Setup;
using SpaceNotch.Infrastructure.Logging;
using SpaceNotch.Platform.Windows.Setup;

namespace SpaceNotch_App.Setup;

/// <summary>
/// L'installation sans fenêtre : le processus élevé d'une installation pour
/// tous, et les installations scriptées (<c>--quiet</c>).
///
/// <para>
/// Les codes de sortie suivent la convention de Windows Installer, que les
/// outils de déploiement comprennent : 0 réussi, 1602 annulé par
/// l'utilisateur, 1603 échec.
/// </para>
/// </summary>
internal static class SetupRunner
{
    public const int Succeeded = 0;
    public const int Cancelled = 1602;
    public const int Failed = 1603;

    public static async Task<int> RunAsync(SetupCommand command, string version)
    {
        ArgumentNullException.ThrowIfNull(command);

        MiniLogger.Log($"[SETUP] Sans fenêtre : {SetupWindow.Describe(command)}");

        try
        {
            switch (command.Mode)
            {
                case SetupMode.InstallWorker:
                {
                    InstallLayout layout = InstallLayout.For(command.Options.Scope, WindowsSetup.Folders());
                    string source = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable.");

                    await Task.Run(() => WindowsSetup.InstallFiles(layout, command.Options, source, version, null, Log)).ConfigureAwait(false);
                    return Succeeded;
                }

                case SetupMode.UninstallWorker:
                {
                    InstalledProduct? product = WindowsSetup.FindInstalled();

                    if (product is not null)
                    {
                        await Task.Run(() => WindowsSetup.RemoveFiles(product, command.RemoveSettings, Log)).ConfigureAwait(false);
                    }

                    return Succeeded;
                }

                case SetupMode.Install:
                    return Code(await WindowsSetup.InstallAsync(command.Options, version, null, Log).ConfigureAwait(false));

                case SetupMode.Uninstall:
                {
                    InstalledProduct? product = WindowsSetup.FindInstalled();

                    return product is null
                        ? Succeeded
                        : Code(await WindowsSetup.UninstallAsync(product, command.RemoveSettings, Log).ConfigureAwait(false));
                }

                default:
                    return Succeeded;
            }
        }
        catch (Exception ex)
        {
            MiniLogger.Log("[SETUP] Échec sans fenêtre", ex);
            return Failed;
        }
    }

    private static int Code(SetupOutcome outcome) => outcome switch
    {
        SetupOutcome.Succeeded => Succeeded,
        SetupOutcome.ElevationDeclined => Cancelled,
        _ => Failed
    };

    private static void Log(string message) => MiniLogger.Log(message);
}
