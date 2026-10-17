using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using SpaceNotch.Core.Setup;
using SpaceNotch.Platform.Windows.System;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>Issue d'une installation ou d'une désinstallation.</summary>
public enum SetupOutcome
{
    /// <summary>Terminé.</summary>
    Succeeded = 0,

    /// <summary>L'utilisateur a refusé l'élévation demandée par Windows.</summary>
    ElevationDeclined,

    /// <summary>Échec ; le détail est dans le journal.</summary>
    Failed
}

/// <summary>
/// L'installation sur Windows : copie, raccourcis, Paramètres › Applications,
/// lancement au démarrage — et leur exact inverse. Le plan vient du cœur
/// (<see cref="InstallLayout"/>, <see cref="UninstallEntry"/>) ; ici, seulement
/// les gestes. Voir ADR-022.
///
/// <para>
/// <b>L'interface n'est jamais élevée.</b> Pour une installation pour tous,
/// seul le travail d'administrateur passe dans un processus élevé, sans fenêtre
/// (<see cref="SetupMode.InstallWorker"/>). Le lancement au démarrage — une
/// préférence de l'utilisateur, dans sa ruche — et le premier lancement de la
/// notch restent dans le processus de l'utilisateur : une notch lancée depuis un
/// processus élevé tournerait elle-même élevée.
/// </para>
/// </summary>
public static class WindowsSetup
{
    private const int ErrorCancelled = 1223;
    private const int CopyBlock = 1 << 20;

    /// <summary>Dossiers de Windows pour ce compte.</summary>
    public static SystemFolders Folders()
        => new(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
            Path.GetTempPath());

    /// <summary>Vrai si ce processus a les droits d'administrateur.</summary>
    public static bool IsElevated
    {
        get
        {
            try
            {
                using WindowsIdentity identity = WindowsIdentity.GetCurrent();
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// SpaceNotch installée sur ce PC, pour ce compte d'abord, pour tous
    /// ensuite ; <c>null</c> si rien n'est installé.
    /// </summary>
    public static InstalledProduct? FindInstalled()
        => FindInstalled(InstallScope.CurrentUser) ?? FindInstalled(InstallScope.AllUsers);

    /// <summary>
    /// Les choix d'une installation tels qu'ils sont <em>aujourd'hui</em>, pour
    /// une mise à jour : l'inscription au démarrage et le raccourci du bureau
    /// lus sur la machine, pas les cases cochées à l'installation. Une mise à
    /// jour rallumait sinon un démarrage éteint dans les Réglages, et recréait
    /// un raccourci supprimé (n° 17).
    /// </summary>
    public static InstallOptions CurrentChoices(InstalledProduct product)
    {
        ArgumentNullException.ThrowIfNull(product);

        InstallLayout layout = InstallLayout.For(product.Options.Scope, Folders());

        return product.Options with
        {
            StartWithWindows = StartupRegistration.IsRegistered(StartupRegistration.BuildCommand(product.Executable)),
            DesktopShortcut = File.Exists(layout.DesktopShortcut),

            // Activées après coup depuis les Réglages (n° 12) : une installation
            // pour tous n'a pas pu le noter dans la ruche de l'ordinateur ; la
            // notch qui se met à jour porte l'identité, c'est la preuve.
            WindowsNotifications = product.Options.WindowsNotifications || IdentityPackage.HasIdentity
        };
    }

    /// <summary>SpaceNotch installée dans une portée précise, ou <c>null</c>.</summary>
    public static InstalledProduct? FindInstalled(InstallScope only)
    {
        foreach (InstallScope scope in new[] { only })
        {
            try
            {
                using RegistryKey root = OpenHive(scope);
                using RegistryKey? key = root.OpenSubKey(SetupIdentity.UninstallKeyPath, writable: false);

                if (key is null)
                {
                    continue;
                }

                InstalledProduct? product = UninstallEntry.Read(name => key.GetValue(name), scope);

                if (product is not null)
                {
                    return product;
                }
            }
            catch (Exception)
            {
                // Une ruche illisible : on regarde l'autre.
            }
        }

        return null;
    }

    /// <summary>Temps accordé à la vérification du paquet d'identité avant de passer outre.</summary>
    private static readonly TimeSpan RepairTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// Installe. Pour tous et sans droits, relance ce même exécutable élevé
    /// pour le travail d'administrateur, puis termine ici ce qui appartient à
    /// l'utilisateur.
    /// </summary>
    /// <param name="options">Choix de l'installeur.</param>
    /// <param name="version">Version installée.</param>
    /// <param name="progress">Étapes, pour l'interface.</param>
    /// <param name="log">Journal.</param>
    public static async Task<SetupOutcome> InstallAsync(
        InstallOptions options,
        string version,
        IProgress<SetupProgress>? progress,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        InstallLayout layout = InstallLayout.For(options.Scope, Folders());
        string source = Environment.ProcessPath ?? throw new InvalidOperationException("Exécutable introuvable.");

        try
        {
            log?.Invoke($"[SETUP] Installation {version} : portée {options.Scope}, élévation {(layout.RequiresElevation ? "requise" : "inutile")}, dossier {layout.Directory}");

            // D'abord, un paquet d'identité laissé cassé par une version précédente
            // est retiré : sinon la notch installée refuserait de démarrer. Hors du
            // fil de l'interface et borné dans le temps : le gestionnaire de paquets
            // de Windows peut répondre très lentement, l'installation ne l'attend pas.
            //
            // La notch en cours est fermée AVANT : Windows ne retire pas le paquet
            // d'une application qui tourne encore avec lui, il attend qu'elle sorte.
            // C'est ce qui figeait la mise à jour 1.5.0 → 1.13.2 sur « Préparation… ».
            progress?.Report(new SetupProgress(InstallStep.Stopping));
            StopRunning(log);
            Task<int> repair = Task.Run(() => IdentityPackage.RepairAsync(log));

            if (await Task.WhenAny(repair, Task.Delay(RepairTimeout)).ConfigureAwait(false) != repair)
            {
                log?.Invoke("[IDENTITÉ] Vérification du paquet trop longue : abandonnée, l'installation continue.");
            }

            if (layout.RequiresElevation && !IsElevated)
            {
                progress?.Report(new SetupProgress(InstallStep.Preparing, Indeterminate: true));

                var worker = new SetupCommand(SetupMode.InstallWorker, options, Quiet: true);
                int? exit = await RunElevatedAsync(source, worker.ToCommandLine()).ConfigureAwait(false);

                if (exit is null)
                {
                    return SetupOutcome.ElevationDeclined;
                }

                if (exit != 0)
                {
                    log?.Invoke($"[SETUP] Le processus élevé a échoué (code {exit}).");
                    return SetupOutcome.Failed;
                }

                progress?.Report(new SetupProgress(InstallStep.Registering));
            }
            else
            {
                await Task.Run(() => InstallFiles(layout, options, source, version, progress, log)).ConfigureAwait(false);
            }

            // Identité de paquet (notifications), pour l'utilisateur lui-même :
            // enregistrée depuis son processus, jamais depuis le processus élevé,
            // qui pourrait appartenir à un autre compte. Un échec n'empêche pas
            // l'installation : SpaceNotch tourne, sans les notifications Windows.
            progress?.Report(new SetupProgress(InstallStep.Registering));

            // Facultatif (phase C) : sans notifications Windows, ni certificat
            // ni paquet d'identité — donc aucune demande d'autorisation.
            if (options.WindowsNotifications)
            {
                await TrustIdentityCertificateAsync(layout, source, log).ConfigureAwait(false);
                await IdentityPackage.RegisterAsync(
                    layout.Directory,
                    Path.Combine(layout.Directory, IdentityPackage.FileName),
                    log).ConfigureAwait(false);
            }
            else
            {
                log?.Invoke("[IDENTITÉ] Notifications Windows non demandées : certificat et paquet ignorés.");
            }

            // Préférence de l'utilisateur, dans sa propre ruche, jamais élevée.
            StartupRegistration.SetEnabled(options.StartWithWindows, StartupRegistration.BuildCommand(layout.Executable), out string? error);

            if (error is not null)
            {
                log?.Invoke($"[SETUP] Lancement au démarrage non inscrit : {error}");
            }

            progress?.Report(new SetupProgress(InstallStep.Done));
            return SetupOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[SETUP] Installation interrompue : {ex}");
            return SetupOutcome.Failed;
        }
    }

    /// <summary>
    /// Active les notifications Windows après coup (n° 12) : choisies « non » à
    /// l'installation, rien ne permettait de les obtenir ensuite. Le paquet
    /// d'identité et son certificat sont toujours copiés dans le dossier
    /// d'installation : le certificat est approuvé (une demande d'accord à
    /// Windows), le paquet enregistré, et le choix noté pour les mises à jour.
    /// La notch doit ensuite redémarrer : l'identité se prend au lancement.
    /// </summary>
    public static async Task<bool> EnableNotificationsAsync(InstalledProduct product, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            InstallLayout layout = InstallLayout.For(product.Options.Scope, Folders());
            string package = Path.Combine(layout.Directory, IdentityPackage.FileName);

            if (!File.Exists(package) || !File.Exists(Path.Combine(layout.Directory, IdentityPackage.CertificateFileName)))
            {
                log?.Invoke("[IDENTITÉ] Paquet ou certificat d'identité absent du dossier d'installation : notifications impossibles.");
                return false;
            }

            // L'exécutable de la machine, pas celui que dit le registre.
            await TrustIdentityCertificateAsync(layout, layout.Executable, log).ConfigureAwait(false);

            if (!IdentityPackage.IsCertificateTrusted())
            {
                log?.Invoke("[IDENTITÉ] Certificat non approuvé : notifications non activées.");
                return false;
            }

            if (!await IdentityPackage.RegisterAsync(layout.Directory, package, log).ConfigureAwait(false))
            {
                return false;
            }

            // Noté pour les mises à jour ; une installation pour tous garde son
            // entrée dans la ruche de l'ordinateur, que l'on ne réécrit pas sans
            // droits : CurrentChoices lit l'identité de la notch qui se met à jour.
            if (product.Options.Scope == InstallScope.CurrentUser)
            {
                using RegistryKey? key = Registry.CurrentUser.OpenSubKey(SetupIdentity.UninstallKeyPath, writable: true);
                key?.SetValue(UninstallEntry.NotificationsValue, 1, RegistryValueKind.DWord);
            }

            log?.Invoke("[IDENTITÉ] Notifications Windows activées après l'installation : redémarrage de la notch à faire.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[IDENTITÉ] Activation des notifications impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Lance la notch installée, qui attendra la sortie de ce processus avant de
    /// prendre la place (n° 12, <see cref="RestartArgument"/>).
    /// </summary>
    public static bool RelaunchAfterExit(string executable)
    {
        if (!File.Exists(executable))
        {
            return false;
        }

        Launch(executable, RestartArgument.For(Environment.ProcessId));
        return true;
    }

    /// <summary>
    /// Approuve le certificat du paquet d'identité pour l'ordinateur : Windows
    /// l'exige pour un certificat auto-signé. Directement si l'on est déjà élevé
    /// (installation pour tous), sinon par un processus élevé qui ne fait que
    /// cela. Refusé ou échoué : SpaceNotch s'installe quand même, sans les
    /// notifications Windows.
    /// </summary>
    private static async Task TrustIdentityCertificateAsync(InstallLayout layout, string source, Action<string>? log)
    {
        string certificate = Path.Combine(layout.Directory, IdentityPackage.CertificateFileName);

        if (!File.Exists(certificate) || IdentityPackage.IsCertificateTrusted())
        {
            return;
        }

        if (IsElevated)
        {
            IdentityPackage.TrustCertificate(certificate, log);
            return;
        }

        var worker = new SetupCommand(SetupMode.TrustIdentityWorker, new InstallOptions(layout.Scope, false, false), Quiet: true);
        int? exit = await RunElevatedAsync(source, worker.ToCommandLine()).ConfigureAwait(false);

        log?.Invoke(exit switch
        {
            null => "[IDENTITÉ] Approbation du certificat refusée par l'utilisateur : pas de notifications Windows.",
            0 => "[IDENTITÉ] Certificat approuvé par le processus élevé.",
            _ => $"[IDENTITÉ] Le processus élevé n'a pas approuvé le certificat (code {exit})."
        });
    }

    /// <summary>
    /// Le travail sur disque et dans le registre de la portée : ce que fait le
    /// processus élevé d'une installation pour tous, et le processus de
    /// l'utilisateur pour une installation personnelle.
    /// </summary>
    public static void InstallFiles(
        InstallLayout layout,
        InstallOptions options,
        string source,
        string version,
        IProgress<SetupProgress>? progress,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(options);

        progress?.Report(new SetupProgress(InstallStep.Preparing));
        Directory.CreateDirectory(layout.Directory);

        progress?.Report(new SetupProgress(InstallStep.Stopping));
        StopRunning(log);

        progress?.Report(new SetupProgress(InstallStep.Copying));
        CopyExecutable(source, layout.Executable, within => progress?.Report(new SetupProgress(InstallStep.Copying, within)));

        // L'installeur est livré sans déclaration d'identité (il doit pouvoir
        // démarrer même si l'enregistrement est cassé) ; la copie installée la
        // reçoit, pour les notifications Windows. Échec : pas de notifications,
        // la notch tourne quand même.
        MakeInstalledVariant(source, layout.Executable, version, log);

        // Le paquet d'identité voyage avec l'exécutable : on le garde dans le
        // dossier d'installation, où l'enregistrement — et un réenregistrement
        // plus tard — le trouvent.
        if (IdentityPackage.BundledPackage is { } identity)
        {
            File.Copy(identity, Path.Combine(layout.Directory, IdentityPackage.FileName), overwrite: true);

            string certificate = Path.Combine(Path.GetDirectoryName(identity)!, IdentityPackage.CertificateFileName);

            if (File.Exists(certificate))
            {
                File.Copy(certificate, Path.Combine(layout.Directory, IdentityPackage.CertificateFileName), overwrite: true);
            }
        }

        progress?.Report(new SetupProgress(InstallStep.Shortcuts));
        ShellLink.Create(layout.StartMenuShortcut, layout.Executable, SetupIdentity.ShortcutDescription);

        if (options.DesktopShortcut)
        {
            ShellLink.Create(layout.DesktopShortcut, layout.Executable, SetupIdentity.ShortcutDescription);
        }
        else
        {
            ShellLink.Delete(layout.DesktopShortcut);
        }

        progress?.Report(new SetupProgress(InstallStep.Registering));
        long size = new FileInfo(layout.Executable).Length;

        using RegistryKey root = OpenHive(layout.Scope);
        using RegistryKey key = root.CreateSubKey(SetupIdentity.UninstallKeyPath, writable: true)
            ?? throw new InvalidOperationException("Clé de désinstallation inaccessible.");

        foreach (RegistryValue value in UninstallEntry.Values(layout, options, version, size, DateOnly.FromDateTime(DateTime.Now)))
        {
            if (value.Number is int number)
            {
                key.SetValue(value.Name, number, RegistryValueKind.DWord);
            }
            else
            {
                key.SetValue(value.Name, value.Text ?? string.Empty, RegistryValueKind.String);
            }
        }

        log?.Invoke($"[SETUP] Installée dans {layout.Directory} ({layout.Scope}).");
    }

    /// <summary>
    /// Désinstalle. Pour une installation pour tous et sans droits, le travail
    /// d'administrateur passe dans un processus élevé.
    /// </summary>
    public static async Task<SetupOutcome> UninstallAsync(
        InstalledProduct product,
        bool removeSettings,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        try
        {
            // Préférence de l'utilisateur : retirée ici, dans sa ruche, avec la
            // marque « désactivée » que Windows aurait posée : une réinstallation
            // qui demande le démarrage le retrouverait sinon éteint.
            StartupRegistration.SetEnabled(false, StartupRegistration.BuildCommand(product.Executable), out _);
            StartupRegistration.ClearWindowsDisable();

            if (product.Options.Scope == InstallScope.AllUsers && !IsElevated)
            {
                string self = Environment.ProcessPath ?? product.Executable;
                var worker = new SetupCommand(SetupMode.UninstallWorker, product.Options, Quiet: true, removeSettings, Environment.ProcessId);
                int? exit = await RunElevatedAsync(self, worker.ToCommandLine()).ConfigureAwait(false);

                if (exit is null)
                {
                    return SetupOutcome.ElevationDeclined;
                }

                if (exit != 0)
                {
                    // Sans cette ligne, l'écran d'échec montrait la dernière
                    // réussite notée comme raison (relecture de #96).
                    log?.Invoke($"[SETUP] Le processus élevé a échoué (code {exit}).");
                    return SetupOutcome.Failed;
                }

                // Le processus élevé n'a effacé que le dossier de la machine : les
                // dossiers de l'utilisateur partent depuis son propre processus.
                StartCleanup(InstallLayout.UserLeftovers(Folders(), removeSettings), []);
                ForgetUser(removeSettings, log);
                return SetupOutcome.Succeeded;
            }

            await Task.Run(() => RemoveFiles(product, removeSettings, log)).ConfigureAwait(false);
            ForgetUser(removeSettings, log);
            return SetupOutcome.Succeeded;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[SETUP] Désinstallation interrompue : {ex}");
            return SetupOutcome.Failed;
        }
    }

    /// <summary>
    /// Retire raccourcis et entrée de Paramètres › Applications, puis confie à
    /// l'interpréteur de commandes l'effacement des dossiers, une fois ce
    /// processus sorti.
    /// </summary>
    /// <param name="product">L'installation à retirer.</param>
    /// <param name="removeSettings">Effacer aussi réglages et données de l'utilisateur.</param>
    /// <param name="log">Journal.</param>
    /// <param name="callerProcessId">Processus de l'utilisateur à attendre (processus élevé).</param>
    /// <param name="userFolders">
    /// Faux dans le processus élevé : il ne connaît pas le profil de
    /// l'utilisateur (souvent un autre compte), qui efface les siens lui-même.
    /// </param>
    public static void RemoveFiles(InstalledProduct product, bool removeSettings, Action<string>? log = null, int? callerProcessId = null, bool userFolders = true)
    {
        ArgumentNullException.ThrowIfNull(product);

        SystemFolders folders = Folders();

        // Le dossier effacé est celui que SpaceNotch s'est choisi, jamais celui
        // que prétend le registre : la valeur peut avoir été modifiée, et cet
        // effacement se fait parfois avec les droits d'administrateur.
        InstallLayout layout = InstallLayout.For(product.Options.Scope, folders);
        bool ownFolder = layout.IsExpectedDirectory(product.Directory);

        if (!ownFolder)
        {
            log?.Invoke($"[SETUP] Dossier inscrit inattendu, conservé : {product.Directory}");
        }

        StopRunning(log, callerProcessId);

        ShellLink.Delete(layout.StartMenuShortcut);
        ShellLink.Delete(layout.DesktopShortcut);

        using (RegistryKey root = OpenHive(layout.Scope))
        {
            root.DeleteSubKeyTree(SetupIdentity.UninstallKeyPath, throwOnMissingSubKey: false);
        }

        List<string> leftovers = (userFolders ? layout.LeftoversAfterExit(folders, removeSettings) : [layout.Directory]).ToList();

        if (!ownFolder)
        {
            leftovers.Remove(layout.Directory);
        }

        StartCleanup(leftovers, [IdentityPackage.RemoveCommand]);

        log?.Invoke($"[SETUP] Désinstallée de {layout.Directory}.");
    }

    /// <summary>Confie à l'interpréteur de commandes l'effacement, une fois ce processus sorti.</summary>
    private static void StartCleanup(IEnumerable<string> directories, IEnumerable<string> afterExit)
    {
        var cleanup = new ProcessStartInfo("cmd.exe", SelfDelete.Arguments(directories, afterExit))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        };

        using Process? _ = Process.Start(cleanup);
    }

    /// <summary>
    /// Ce qui appartient à l'utilisateur hors de ses dossiers, retiré depuis son
    /// processus, et seulement une fois la désinstallation réussie : refusée ou
    /// échouée, SpaceNotch reste, avec ses clés et ses hooks (relecture de #96).
    /// </summary>
    private static void ForgetUser(bool removeSettings, Action<string>? log)
    {
        // Les clés (Claude, Spotify, Discord) sont dans le coffre de l'utilisateur.
        if (removeSettings)
        {
            PurgeVault(log);
        }

        // Les hooks de Claude Code appelleraient un SpaceNotch.exe disparu, et
        // Claude Code afficherait une erreur à chaque tour (n° 16).
        RemoveAgentHooks(log);
    }

    /// <summary>
    /// Retire les hooks de SpaceNotch de <c>~/.claude/settings.json</c>, en
    /// gardant les autres et une copie du fichier d'origine, comme le bouton des
    /// Réglages. Un fichier illisible n'est pas touché.
    /// </summary>
    private static void RemoveAgentHooks(Action<string>? log)
    {
        string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "settings.json");

        try
        {
            if (!File.Exists(path))
            {
                return;
            }

            string current = File.ReadAllText(path);

            if (!SpaceNotch.Core.Channel.ClaudeHook.IsInstalled(current))
            {
                return;
            }

            // Calculé d'abord : un fichier illisible lève ici, avant toute écriture.
            string next = SpaceNotch.Core.Channel.ClaudeHook.Uninstall(current);
            File.Copy(path, path + ".spacenotch.bak", overwrite: true);
            string temporary = path + ".spacenotch.tmp";
            File.WriteAllText(temporary, next);
            File.Move(temporary, path, overwrite: true);
            log?.Invoke("[SETUP] Hooks de Claude Code retirés (une session Claude Code déjà ouverte les garde jusqu'à son redémarrage).");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[SETUP] Hooks de Claude Code non retirés : {ex.Message}");

            try
            {
                File.Delete(path + ".spacenotch.tmp");
            }
            catch (IOException)
            {
                // Rien à nettoyer de plus.
            }
            catch (UnauthorizedAccessException)
            {
                // Rien à nettoyer de plus.
            }
        }
    }

    /// <summary>Retire du coffre de Windows tous les identifiants de SpaceNotch.</summary>
    private static void PurgeVault(Action<string>? log)
    {
        try
        {
            var vault = new global::Windows.Security.Credentials.PasswordVault();
            int removed = 0;

            foreach (global::Windows.Security.Credentials.PasswordCredential credential in vault.RetrieveAll())
            {
                if (credential.Resource.StartsWith(SetupIdentity.VaultPrefix, StringComparison.Ordinal))
                {
                    vault.Remove(credential);
                    removed++;
                }
            }

            log?.Invoke($"[SETUP] Coffre : {removed} identifiant(s) retiré(s).");
        }
        catch (Exception ex)
        {
            // Coffre vide ou illisible : rien à retirer.
            log?.Invoke($"[SETUP] Coffre non purgé : {ex.Message}");
        }
    }

    /// <summary>Lance la notch installée, dans le processus de l'utilisateur.</summary>
    public static void Launch(string executable, string? argument = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
        };

        if (argument is not null)
        {
            start.ArgumentList.Add(argument);
        }

        using Process? _ = Process.Start(start);
    }

    /// <summary>
    /// Ferme la notch en cours : on ne remplace pas un exécutable qui tourne.
    /// Ses réglages sont déjà enregistrés, elle n'a rien à sauver.
    /// </summary>
    public static void StopRunning(Action<string>? log = null, int? keepProcessId = null)
    {
        int self = Environment.ProcessId;

        foreach (Process process in Process.GetProcessesByName(SetupIdentity.ProcessName))
        {
            using (process)
            {
                // Ni ce processus, ni l'interface qui attend sa fin.
                if (process.Id == self || process.Id == keepProcessId)
                {
                    continue;
                }

                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(5000);
                }
                catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
                {
                    log?.Invoke($"[SETUP] Impossible de fermer le processus {process.Id} : {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Relance cet exécutable avec les droits d'administrateur et attend sa fin.
    /// Renvoie son code de sortie, ou <c>null</c> si l'utilisateur a refusé.
    /// </summary>
    public static async Task<int?> RunElevatedAsync(string executable, string arguments)
    {
        var start = new ProcessStartInfo(executable, arguments)
        {
            UseShellExecute = true,
            Verb = "runas",
            WindowStyle = ProcessWindowStyle.Hidden
        };

        try
        {
            using Process? process = Process.Start(start);

            if (process is null)
            {
                return -1;
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return null;
        }
    }

    /// <summary>Supprime un fichier s'il le peut : un reste de copie ne doit pas faire échouer l'installation.</summary>
    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un reste de copie inoffensif : la prochaine installation l'écrase.
        }
    }

    /// <summary>
    /// Fait de la copie la variante installée (n° 51). Non signée, l'identité est
    /// rallumée sur place, comme avant. Signée, elle n'est jamais modifiée sur
    /// place : l'installée est refaite à côté, à l'identique de celle que la CI a
    /// signée, et ne remplace la copie que si son empreinte est la bonne. Sinon
    /// la copie reste identité éteinte : pas de notifications Windows, mais une
    /// signature intacte. Jamais un exécutable à la signature cassée, et jamais
    /// une installation qui échoue pour cela.
    /// </summary>
    private static void MakeInstalledVariant(string source, string installed, string version, Action<string>? log)
    {
        if (IdentityManifestFile.IsOn(installed))
        {
            return;
        }

        if (InstalledVariant.Plan(InstalledVariantFile.IsSigned(installed), haveSignature: false) == VariantPlan.FlipInPlace)
        {
            IdentityManifestFile.Set(installed, on: true, log);
            return;
        }

        // À côté de l'installeur d'abord (une mise à jour les y a posés), sauf en
        // processus élevé : ce dossier, l'utilisateur peut l'écrire. Puis la release.
        if ((!IsElevated && TryRebuild(installed, InstalledVariantFile.FindBeside(source, log), log))
            || TryRebuild(installed, InstalledVariantFile.FindOnline(version, log), log))
        {
            return;
        }

        log?.Invoke("[IDENTITÉ] Exécutable signé, sans la signature de la variante installée : identité éteinte, signature intacte.");
    }

    /// <summary>Refait l'installée sur une copie, et ne la met en place que vérifiée.</summary>
    private static bool TryRebuild(string installed, (VariantSignature Signature, string Sha256)? variant, Action<string>? log)
    {
        if (variant is not { } found)
        {
            return false;
        }

        string staging = installed + ".variant";

        try
        {
            File.Copy(installed, staging, overwrite: true);

            if (!InstalledVariantFile.Rebuild(staging, found.Signature, found.Sha256, log))
            {
                return false;
            }

            File.Move(staging, installed, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"[IDENTITÉ] Variante installée non mise en place : {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                File.Delete(staging);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Un reste à côté ne gêne rien : il sera écrasé à la prochaine installation.
            }
        }
    }

    /// <summary>
    /// Copie à côté, puis remplace : une copie interrompue ne laisse jamais un
    /// exécutable à moitié écrit à la place de l'ancien. Quelques essais, le
    /// temps qu'un processus fermé relâche son fichier.
    /// </summary>
    private static void CopyExecutable(string source, string destination, Action<double> copied)
    {
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        string staging = destination + ".new";

        try
        {
            // Par blocs, pour que la barre avance avec la copie : l'exécutable
            // pèse quelques centaines de mégaoctets.
            using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBlock);
            using var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, CopyBlock);
            byte[] buffer = new byte[CopyBlock];
            long total = Math.Max(1, input.Length);
            long done = 0;
            int read;

            output.SetLength(input.Length);

            while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
            {
                output.Write(buffer, 0, read);
                done += read;
                copied((double)done / total);
            }
        }
        catch
        {
            // Disque plein, copie interrompue : l'exécutable en place n'a pas été
            // touché (n° 7), seul le fichier à moitié copié s'en va.
            TryDelete(staging);
            throw;
        }

        for (int attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(staging, destination, overwrite: true);
                return;
            }
            catch (IOException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch (UnauthorizedAccessException) when (attempt < 10)
            {
                Thread.Sleep(300);
            }
            catch
            {
                // L'exécutable en place reste verrouillé : il n'est pas remplacé,
                // et la copie prête ne traîne pas dans le dossier (n° 7).
                TryDelete(staging);
                throw;
            }
        }
    }

    private static RegistryKey OpenHive(InstallScope scope)
        => RegistryKey.OpenBaseKey(
            scope == InstallScope.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
            RegistryView.Registry64);
}
