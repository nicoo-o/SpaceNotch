using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>
/// Le paquet d'identité de SpaceNotch (ADR-023) : un paquet non signé « à
/// emplacement externe » qui donne à <c>SpaceNotch.exe</c> installé une
/// identité de paquet — sans elle, Windows refuse l'écoute des notifications.
///
/// <para>
/// Enregistré par l'installeur pour l'utilisateur, retiré par le
/// désinstalleur. L'exécutable portable n'en a pas : il tourne comme avant,
/// sans les notifications Windows.
/// </para>
/// </summary>
public static partial class IdentityPackage
{
    public const string PackageName = "SpaceNotch.Identity";

    /// <summary>L'OID marque un paquet non signé : Windows 11 l'accepte avec <c>AllowUnsigned</c>.</summary>
    public const string Publisher = "CN=SpaceNotch, OID.2.25.311729368913984317654407730594956997722=1";

    /// <summary>Nom du paquet livré avec l'application (produit par le workflow Release).</summary>
    public const string FileName = "SpaceNotch.Identity.msix";

    private const int AppModelErrorNoPackage = 15700;

    /// <summary>
    /// Retrait joué par la fin de la désinstallation, une fois SpaceNotch sorti :
    /// le désinstalleur porte lui-même cette identité, et Windows fermerait un
    /// processus qui retire son propre paquet.
    /// </summary>
    public const string RemoveCommand =
        "powershell -NoProfile -NonInteractive -WindowStyle Hidden -Command \"Get-AppxPackage -Name " + PackageName + " | Remove-AppxPackage\" > nul 2>&1";

    /// <summary>Vrai si ce processus tourne avec une identité de paquet.</summary>
    public static bool HasIdentity
    {
        get
        {
            try
            {
                uint length = 0;
                int result = GetCurrentPackageFullName(ref length, IntPtr.Zero);
                return result != AppModelErrorNoPackage;
            }
            catch (EntryPointNotFoundException)
            {
                return false;
            }
        }
    }

    /// <summary>Le paquet livré à côté de l'application en cours, s'il y en a un.</summary>
    public static string? BundledPackage
    {
        get
        {
            string path = Path.Combine(AppContext.BaseDirectory, FileName);
            return File.Exists(path) ? path : null;
        }
    }

    /// <summary>
    /// Enregistre l'identité pour l'utilisateur courant, en la liant au dossier
    /// d'installation. Idempotent : une version plus récente remplace l'ancienne.
    /// </summary>
    /// <returns>Vrai si l'identité est enregistrée.</returns>
    public static async Task<bool> RegisterAsync(string installDirectory, string packagePath, Action<string>? log = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(packagePath);

        if (!File.Exists(packagePath))
        {
            log?.Invoke($"[IDENTITÉ] Paquet absent : {packagePath}");
            return false;
        }

        try
        {
            var manager = new PackageManager();
            var options = new AddPackageOptions
            {
                // Le dossier exact où se trouve SpaceNotch.exe : un autre, et
                // l'exécutable tournerait sans identité.
                ExternalLocationUri = new Uri(Path.TrimEndingDirectorySeparator(installDirectory) + Path.DirectorySeparatorChar),
                AllowUnsigned = true,
                ForceUpdateFromAnyVersion = true
            };

            DeploymentResult result = await manager.AddPackageByUriAsync(new Uri(packagePath), options);

            if (result.ExtendedErrorCode is { } error && error.HResult != 0)
            {
                log?.Invoke($"[IDENTITÉ] Enregistrement refusé : 0x{error.HResult:X8} {result.ErrorText}");
                return false;
            }

            log?.Invoke("[IDENTITÉ] Paquet d'identité enregistré.");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[IDENTITÉ] Enregistrement impossible : {ex.Message}");
            return false;
        }
    }

    /// <summary>Retire l'identité de l'utilisateur courant. Sans effet si elle n'est pas là.</summary>
    public static async Task RemoveAsync(Action<string>? log = null)
    {
        try
        {
            var manager = new PackageManager();

            foreach (global::Windows.ApplicationModel.Package package in manager.FindPackagesForUser(string.Empty, PackageName, Publisher).ToList())
            {
                DeploymentResult result = await manager.RemovePackageAsync(package.Id.FullName);

                log?.Invoke(result.ExtendedErrorCode is { } error && error.HResult != 0
                    ? $"[IDENTITÉ] Retrait refusé : 0x{error.HResult:X8} {result.ErrorText}"
                    : "[IDENTITÉ] Paquet d'identité retiré.");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"[IDENTITÉ] Retrait impossible : {ex.Message}");
        }
    }

    [LibraryImport("kernel32.dll")]
    private static partial int GetCurrentPackageFullName(ref uint length, IntPtr name);
}
