using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using SpaceNotch.Core.Setup;
using SpaceNotch.Core.Update;

namespace SpaceNotch.Platform.Windows.Update;

/// <summary>Un installeur téléchargé et son empreinte vérifiée.</summary>
public sealed record DownloadedUpdate(ReleaseInfo Release, string SetupPath, string Sha256);

/// <summary>
/// Le réseau et le disque de la mise à jour automatique : lire la dernière
/// release, télécharger l'installeur, vérifier son empreinte SHA-256, lancer
/// l'installation silencieuse. Les décisions sont dans
/// <see cref="UpdateRules"/>.
/// </summary>
public sealed class UpdateClient : IDisposable
{
    private readonly HttpClient _http;

    public UpdateClient(string currentVersion)
    {
        _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

        // GitHub refuse les requêtes sans agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"SpaceNotch/{currentVersion}");
        _http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Dossier des installeurs téléchargés.</summary>
    public static string UpdatesDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        SetupIdentity.ProductName,
        "updates");

    /// <summary>La dernière release publiée, ou <c>null</c> (hors ligne, limite atteinte, release inutilisable).</summary>
    public async Task<ReleaseInfo?> LatestAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, UpdateRules.LatestReleaseApi);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(20));
            using HttpResponseMessage response = await _http.SendAsync(request, cts.Token).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return UpdateRules.ParseRelease(json);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    /// <summary>Chemin de l'installeur téléchargé et vérifié pour une version.</summary>
    public static string SetupPathFor(Version version)
        => Path.Combine(UpdatesDirectory, UpdateRules.Display(version), UpdateRules.SetupAsset);

    /// <summary>
    /// Télécharge l'installeur d'une release et vérifie son empreinte. Rend le
    /// chemin du fichier vérifié, ou <c>null</c> : sans fichier d'empreintes,
    /// ou si l'empreinte ou la taille ne correspondent pas, rien n'est gardé.
    /// </summary>
    public async Task<DownloadedUpdate?> DownloadAsync(ReleaseInfo release, Action<string>? log = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(release);

        string target = SetupPathFor(release.Version);

        if (release.ChecksumsUrl is null)
        {
            log?.Invoke($"[MISE À JOUR] {release.Tag} sans fichier d'empreintes : pas d'installation automatique.");
            return null;
        }

        try
        {
            string sums = await _http.GetStringAsync(release.ChecksumsUrl, cancellationToken).ConfigureAwait(false);

            if (UpdateRules.FindChecksum(sums, UpdateRules.SetupAsset) is not { } expected)
            {
                log?.Invoke($"[MISE À JOUR] Empreinte de l'installeur absente de {release.Tag}.");
                return null;
            }

            // Déjà téléchargé et intact : rien à refaire.
            if (File.Exists(target) && string.Equals(await HashAsync(target, cancellationToken).ConfigureAwait(false), expected, StringComparison.Ordinal))
            {
                return new DownloadedUpdate(release, target, expected);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            string partial = target + ".part";

            using (HttpResponseMessage response = await _http.GetAsync(release.SetupUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();

                await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var file = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true);
                await source.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            }

            long size = new FileInfo(partial).Length;
            string actual = await HashAsync(partial, cancellationToken).ConfigureAwait(false);

            if (size != release.SetupSize || !string.Equals(actual, expected, StringComparison.Ordinal))
            {
                File.Delete(partial);
                log?.Invoke($"[MISE À JOUR] Installeur {release.Tag} refusé : empreinte ou taille différente.");
                return null;
            }

            File.Move(partial, target, overwrite: true);
            log?.Invoke($"[MISE À JOUR] {release.Tag} téléchargée et vérifiée ({size / 1024 / 1024} Mo).");
            Prune(release.Version);
            return new DownloadedUpdate(release, target, expected);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"[MISE À JOUR] Téléchargement interrompu : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Vérifie de nouveau un installeur juste avant de l'exécuter : il a pu
    /// changer sur le disque depuis son téléchargement.
    /// </summary>
    public static async Task<bool> VerifyAsync(string setupPath, string expectedSha256, CancellationToken cancellationToken = default)
        => File.Exists(setupPath)
            && string.Equals(await HashAsync(setupPath, cancellationToken).ConfigureAwait(false), expectedSha256.ToLowerInvariant(), StringComparison.Ordinal);

    /// <summary>
    /// Lance l'installation silencieuse par-dessus l'installation existante,
    /// avec les mêmes choix, et la relance de la notch à la fin. L'appelant
    /// quitte ensuite : l'installeur remplace un exécutable qui ne tourne plus.
    /// </summary>
    public static bool StartInstall(string setupPath, InstallOptions options, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setupPath);
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            var command = new SetupCommand(SetupMode.Install, options, Quiet: true, Relaunch: true);
            var start = new ProcessStartInfo(setupPath)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(setupPath) ?? string.Empty
            };

            foreach (string argument in command.ToArguments())
            {
                start.ArgumentList.Add(argument);
            }

            using Process? process = Process.Start(start);
            log?.Invoke($"[MISE À JOUR] Installation lancée : {setupPath} {command.ToCommandLine()}");
            return process is not null;
        }
        catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            log?.Invoke($"[MISE À JOUR] Lancement de l'installeur impossible : {ex.Message}");
            return false;
        }
    }

    private static async Task<string> HashAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Ne garde que l'installeur de la version voulue.</summary>
    private static void Prune(Version keep)
    {
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(UpdatesDirectory))
            {
                if (!string.Equals(Path.GetFileName(directory), UpdateRules.Display(keep), StringComparison.OrdinalIgnoreCase))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un vieux dossier qui résiste n'empêche rien.
        }
    }

    public void Dispose() => _http.Dispose();
}
