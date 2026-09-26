using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>
/// Le paquet d'identité de SpaceNotch (ADR-023) : un paquet « à emplacement
/// externe » qui donne à <c>SpaceNotch.exe</c> installé une identité de paquet —
/// sans elle, Windows refuse l'écoute des notifications.
///
/// <para>
/// Signé par le workflow Release avec un certificat éphémère : créé pour la
/// version, sa clé privée disparaît avec la machine de construction. Seul le
/// certificat public voyage avec l'installeur, qui l'approuve pour l'ordinateur
/// (une demande administrateur), puis enregistre le paquet pour l'utilisateur.
/// </para>
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

    /// <summary>Éditeur du paquet — et sujet du certificat qui le signe.</summary>
    public const string Publisher = "CN=SpaceNotch";

    /// <summary>Nom du paquet livré avec l'application (produit par le workflow Release).</summary>
    public const string FileName = "SpaceNotch.Identity.msix";

    /// <summary>Certificat public qui a signé ce paquet-ci.</summary>
    public const string CertificateFileName = "SpaceNotch.Identity.cer";

    /// <summary>Nom lisible du certificat dans le magasin : c'est à lui qu'on reconnaît les nôtres.</summary>
    public const string CertificateFriendlyName = "SpaceNotch (paquet d'identité)";

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

    /// <summary>
    /// Empreinte du certificat qui a signé le paquet de cette version, inscrite
    /// dans l'exécutable par le workflow Release (<c>IdentityThumbprint</c>).
    /// Le processus élevé n'approuve que ce certificat-là : un fichier .cer
    /// remplacé dans le dossier de l'utilisateur serait refusé.
    /// </summary>
    public static string? ExpectedThumbprint
        => global::System.Reflection.Assembly.GetEntryAssembly()?
            .GetCustomAttributes(typeof(global::System.Reflection.AssemblyMetadataAttribute), inherit: false)
            .OfType<global::System.Reflection.AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "IdentityThumbprint")?.Value is { Length: > 0 } thumbprint
            ? thumbprint
            : null;

    /// <summary>Vrai si le certificat de cette version est déjà approuvé pour l'ordinateur.</summary>
    public static bool IsCertificateTrusted()
    {
        if (ExpectedThumbprint is not { } expected)
        {
            return false;
        }

        try
        {
            using var store = new global::System.Security.Cryptography.X509Certificates.X509Store(global::System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople, global::System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
            store.Open(global::System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
            return store.Certificates.Any(c => string.Equals(c.Thumbprint, expected, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Approuve, pour l'ordinateur (magasin « Personnes autorisées » de la
    /// machine — Windows n'accepte pas un certificat auto-signé approuvé pour
    /// l'utilisateur seul), le certificat public de cette version, et retire
    /// ceux des versions précédentes. Exige les droits d'administrateur.
    /// </summary>
    public static bool TrustCertificate(string certificatePath, Action<string>? log = null)
    {
        try
        {
            using var certificate = global::System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificateFromFile(certificatePath);

            if (!string.Equals(certificate.Subject, Publisher, StringComparison.Ordinal)
                || certificate.HasPrivateKey
                || ExpectedThumbprint is not { } expected
                || !string.Equals(certificate.Thumbprint, expected, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"[IDENTITÉ] Certificat refusé (sujet ou empreinte inattendus) : {certificate.Subject} {certificate.Thumbprint}");
                return false;
            }

            certificate.FriendlyName = CertificateFriendlyName;

            using var store = new global::System.Security.Cryptography.X509Certificates.X509Store(global::System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople, global::System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
            store.Open(global::System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);

            foreach (var old in Ours(store))
            {
                if (!string.Equals(old.Thumbprint, certificate.Thumbprint, StringComparison.OrdinalIgnoreCase))
                {
                    store.Remove(old);
                }
            }

            store.Add(certificate);
            log?.Invoke($"[IDENTITÉ] Certificat approuvé : {certificate.Thumbprint}");
            return true;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[IDENTITÉ] Certificat non approuvé : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Retire nos certificats du magasin de l'ordinateur (désinstallation pour
    /// tous, déjà élevée). Laissé ailleurs, un certificat SpaceNotch reste
    /// inoffensif : sa clé privée a été détruite dès la signature.
    /// </summary>
    public static void RemoveCertificates(Action<string>? log = null)
    {
        try
        {
            using var store = new global::System.Security.Cryptography.X509Certificates.X509Store(global::System.Security.Cryptography.X509Certificates.StoreName.TrustedPeople, global::System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
            store.Open(global::System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);

            foreach (var old in Ours(store))
            {
                store.Remove(old);
            }

            log?.Invoke("[IDENTITÉ] Certificat retiré.");
        }
        catch (Exception ex)
        {
            log?.Invoke($"[IDENTITÉ] Retrait du certificat impossible : {ex.Message}");
        }
    }

    /// <summary>Nos certificats : le bon sujet et notre nom lisible — jamais un autre certificat.</summary>
    private static List<global::System.Security.Cryptography.X509Certificates.X509Certificate2> Ours(global::System.Security.Cryptography.X509Certificates.X509Store store)
        => store.Certificates
            .Where(c => string.Equals(c.Subject, Publisher, StringComparison.Ordinal)
                && string.Equals(c.FriendlyName, CertificateFriendlyName, StringComparison.Ordinal))
            .ToList();

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
