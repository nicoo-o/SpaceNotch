using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
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
    {
        foreach (InstallScope scope in new[] { InstallScope.CurrentUser, InstallScope.AllUsers })
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
            // Préférence de l'utilisateur : retirée ici, dans sa ruche.
            StartupRegistration.SetEnabled(false, StartupRegistration.BuildCommand(product.Executable), out _);

            if (product.Options.Scope == InstallScope.AllUsers && !IsElevated)
            {
                string self = Environment.ProcessPath ?? product.Executable;
                var worker = new SetupCommand(SetupMode.UninstallWorker, product.Options, Quiet: true, removeSettings);
                int? exit = await RunElevatedAsync(self, worker.ToCommandLine()).ConfigureAwait(false);

                return exit switch
                {
                    null => SetupOutcome.ElevationDeclined,
                    0 => SetupOutcome.Succeeded,
                    _ => SetupOutcome.Failed
                };
            }

            await Task.Run(() => RemoveFiles(product, removeSettings, log)).ConfigureAwait(false);
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
    public static void RemoveFiles(InstalledProduct product, bool removeSettings, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(product);

        SystemFolders folders = Folders();
        InstallLayout layout = InstallLayout.For(product.Options.Scope, folders) with
        {
            Directory = product.Directory,
            Executable = product.Executable
        };

        StopRunning(log);

        ShellLink.Delete(layout.StartMenuShortcut);
        ShellLink.Delete(layout.DesktopShortcut);

        using (RegistryKey root = OpenHive(layout.Scope))
        {
            root.DeleteSubKeyTree(SetupIdentity.UninstallKeyPath, throwOnMissingSubKey: false);
        }

        IReadOnlyList<string> leftovers = layout.LeftoversAfterExit(folders, removeSettings);

        var cleanup = new ProcessStartInfo("cmd.exe", SelfDelete.Arguments(leftovers))
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WindowStyle = ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetTempPath()
        };

        using Process? _ = Process.Start(cleanup);

        log?.Invoke($"[SETUP] Désinstallée de {layout.Directory}.");
    }

    /// <summary>Lance la notch installée, dans le processus de l'utilisateur.</summary>
    public static void Launch(string executable)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);

        using Process? _ = Process.Start(new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? string.Empty
        });
    }

    /// <summary>
    /// Ferme la notch en cours : on ne remplace pas un exécutable qui tourne.
    /// Ses réglages sont déjà enregistrés, elle n'a rien à sauver.
    /// </summary>
    public static void StopRunning(Action<string>? log = null)
    {
        int self = Environment.ProcessId;

        foreach (Process process in Process.GetProcessesByName(SetupIdentity.ProcessName))
        {
            using (process)
            {
                if (process.Id == self)
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

        // Par blocs, pour que la barre avance avec la copie : l'exécutable
        // pèse quelques centaines de mégaoctets.
        using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, CopyBlock))
        using (var output = new FileStream(staging, FileMode.Create, FileAccess.Write, FileShare.None, CopyBlock))
        {
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
        }
    }

    private static RegistryKey OpenHive(InstallScope scope)
        => RegistryKey.OpenBaseKey(
            scope == InstallScope.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser,
            RegistryView.Registry64);
}
