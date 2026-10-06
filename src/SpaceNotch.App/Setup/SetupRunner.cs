using System;
using System.Threading.Tasks;
using SpaceNotch.Core.Setup;
using SpaceNotch.Core.Update;
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

                case SetupMode.TrustIdentityWorker:
                {
                    // Le seul fichier lu est celui du dossier SpaceNotch de la
                    // portée — jamais un chemin reçu en argument —, et le
                    // certificat doit porter l'empreinte inscrite dans cet
                    // exécutable : un .cer remplacé est refusé.
                    InstallLayout layout = InstallLayout.For(command.Options.Scope, WindowsSetup.Folders());
                    string certificate = System.IO.Path.Combine(layout.Directory, IdentityPackage.CertificateFileName);

                    return IdentityPackage.TrustCertificate(certificate, Log) ? Succeeded : Failed;
                }

                case SetupMode.UninstallWorker:
                {
                    // La portée demandée, et elle seule : lire d'abord la ruche de
                    // l'utilisateur ferait désinstaller la mauvaise installation.
                    InstalledProduct? product = WindowsSetup.FindInstalled(command.Options.Scope);

                    if (product is not null)
                    {
                        await Task.Run(() => WindowsSetup.RemoveFiles(product, command.RemoveSettings, Log, command.CallerProcessId)).ConfigureAwait(false);

                        // Déjà élevé : le certificat SpaceNotch quitte aussi le magasin
                        // de l'ordinateur (clé privée détruite, il reste inoffensif sinon).
                        IdentityPackage.RemoveCertificates(Log);
                    }

                    return Succeeded;
                }

                case SetupMode.Install:
                {
                    int code;

                    try
                    {
                        code = InstallFaultTest()
                            ?? Code(await WindowsSetup.InstallAsync(command.Options, version, null, Log).ConfigureAwait(false));
                    }
                    catch (Exception ex)
                    {
                        MiniLogger.Log("[SETUP] Échec sans fenêtre", ex);
                        code = Failed;
                    }

                    // Mise à jour automatique : la notch a été fermée pour être
                    // remplacée. Elle revient dans tous les cas (n° 7) — nouvelle
                    // version si tout s'est bien passé, sinon la version restée en
                    // place (la copie est atomique), qui dit pourquoi.
                    if (command.Relaunch)
                    {
                        // Une relance impossible ne change pas le résultat de l'installation.
                        try
                        {
                            Relaunch(command.Options.Scope, code);
                        }
                        catch (Exception ex)
                        {
                            MiniLogger.Log("[MISE À JOUR] Relance impossible", ex);
                        }
                    }

                    return code;
                }

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

    private static void Relaunch(InstallScope scope, int code)
    {
        string executable = InstallLayout.For(scope, WindowsSetup.Folders()).Executable;

#if DEBUG
        // L'essai --fault-install ne relance jamais l'installation de l'utilisateur :
        // seulement cette copie de développement.
        if (InstallFaultTest() is not null)
        {
            executable = Environment.ProcessPath ?? executable;
        }
#endif

        switch (UpdateRules.AfterInstall(code, System.IO.File.Exists(executable)))
        {
            case InstallFollowUp.Relaunch:
                Log($"[MISE À JOUR] Relance de {executable}");
                WindowsSetup.Launch(executable);
                break;

            case InstallFollowUp.RelaunchReportingFailure:
                Log($"[MISE À JOUR] Installation en échec (code {code}) : relance de la version en place, {executable}");
                WindowsSetup.Launch(executable, UpdateRules.FailureArgument(code));
                break;

            default:
                Log($"[MISE À JOUR] Rien à relancer (code {code}, {executable} absent)");
                break;
        }
    }

    /// <summary>
    /// Preuve du n° 7 en build Debug (<c>--fault-install</c>) : l'installation
    /// échoue avant de toucher à quoi que ce soit, avec le code du refus
    /// d'élévation. Toujours <c>null</c> en Release.
    /// </summary>
    private static int? InstallFaultTest()
    {
#if DEBUG
        return Environment.GetCommandLineArgs().Contains("--fault-install", StringComparer.OrdinalIgnoreCase)
            ? UpdateRules.DeclinedExitCode
            : null;
#else
        return null;
#endif
    }

    private static int Code(SetupOutcome outcome) => outcome switch
    {
        SetupOutcome.Succeeded => Succeeded,
        SetupOutcome.ElevationDeclined => Cancelled,
        _ => Failed
    };

    private static void Log(string message) => MiniLogger.Log(message);
}
