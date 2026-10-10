using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SpaceNotch.Core.Setup;
using SpaceNotch.Core.Update;

namespace SpaceNotch.Platform.Windows.Setup;

/// <summary>
/// La variante installée sur disque (n° 51, voir <see cref="InstalledVariant"/>) :
/// la reconnaître signée, extraire sa signature (CI, tests), la refaire à partir
/// de la variante téléchargée, et trouver ce qu'il faut pour cela.
/// </summary>
public static class InstalledVariantFile
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>L'en-tête PE du fichier ; <c>null</c> s'il est illisible.</summary>
    public static PeHeader? Header(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var start = new byte[Math.Min(InstalledVariant.HeaderBytes, stream.Length)];
            stream.ReadExactly(start);
            return InstalledVariant.ReadHeader(start);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Vrai si l'exécutable porte une signature Authenticode.</summary>
    public static bool IsSigned(string path) => Header(path) is { HasSignature: true };

    /// <summary>
    /// La signature d'une variante installée, telle que la CI la publie. Un
    /// exécutable non signé donne une table vide.
    /// </summary>
    public static VariantSignature Extract(string installedPath)
    {
        using var stream = new FileStream(installedPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var start = new byte[Math.Min(InstalledVariant.HeaderBytes, stream.Length)];
        stream.ReadExactly(start);

        PeHeader header = InstalledVariant.ReadHeader(start) ?? throw new InvalidDataException($"{installedPath} n'est pas un exécutable PE.");
        uint checksum = BitConverter.ToUInt32(start, header.ChecksumOffset);

        if (!header.HasSignature)
        {
            return new VariantSignature(checksum, 0, []);
        }

        if (header.CertificateOffset + (long)header.CertificateSize != stream.Length || header.CertificateSize > InstalledVariant.MaxCertificate)
        {
            throw new InvalidDataException($"{installedPath} : la table des certificats n'est pas à la fin du fichier.");
        }

        var certificate = new byte[header.CertificateSize];
        stream.Seek(header.CertificateOffset, SeekOrigin.Begin);
        stream.ReadExactly(certificate);
        return new VariantSignature(checksum, header.CertificateOffset, certificate);
    }

    /// <summary>
    /// Refait sur place la variante installée à partir d'une copie de la
    /// variante téléchargée : identité rallumée, sa signature retirée, celle de
    /// l'installée posée. Vrai seulement si le résultat a l'empreinte attendue ;
    /// sinon le fichier est à remplacer (l'appelant recopie l'original).
    /// </summary>
    public static bool Rebuild(string path, VariantSignature signature, string expectedSha256, Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(signature);

        try
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var start = new byte[Math.Min(InstalledVariant.HeaderBytes, stream.Length)];
                stream.ReadExactly(start);

                if (InstalledVariant.ReadHeader(start) is not { } header)
                {
                    log?.Invoke($"[IDENTITÉ] {path} n'est pas un exécutable PE.");
                    return false;
                }

                long image = InstalledVariant.ImageLength(header, stream.Length);
                long target = signature.Certificate.Length > 0 ? signature.CertificateOffset : image;

                // Même image : la table de l'installée commence là où finit la nôtre, au bourrage d'alignement près.
                if (target < image || target - image >= 8)
                {
                    log?.Invoke($"[IDENTITÉ] Signature de la variante installée pour une autre image ({target} ≠ {image}).");
                    return false;
                }

                stream.SetLength(image);
                stream.SetLength(target);

                InstalledVariant.WriteHeader(start, header, signature);
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(start);

                stream.Seek(target, SeekOrigin.Begin);
                stream.Write(signature.Certificate);
                stream.Flush(flushToDisk: true);
            }

            if (!IdentityManifestFile.Set(path, on: true, log))
            {
                return false;
            }

            string actual = Sha256(path);

            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                log?.Invoke($"[IDENTITÉ] Variante installée refaite, mais son empreinte diffère ({actual}).");
                return false;
            }

            log?.Invoke($"[IDENTITÉ] Variante installée refaite, signature intacte : {path}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"[IDENTITÉ] Variante installée non refaite ({path}) : {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// La signature de l'installée et son empreinte, posées à côté de
    /// l'installeur par une mise à jour. Seulement pour un installeur non élevé :
    /// ce dossier, l'utilisateur peut l'écrire ; un processus élevé ne lui fait
    /// pas confiance (voir <see cref="FindOnline"/>). <c>null</c> si l'un manque.
    /// </summary>
    public static (VariantSignature Signature, string Sha256)? FindBeside(string setupPath, Action<string>? log = null)
    {
        string folder = Path.GetDirectoryName(Path.GetFullPath(setupPath)) ?? string.Empty;
        string signaturePath = Path.Combine(folder, InstalledVariant.SignatureAsset);
        string sumsPath = Path.Combine(folder, UpdateRules.ChecksumsAsset);

        try
        {
            if (!File.Exists(signaturePath) || !File.Exists(sumsPath) || new FileInfo(signaturePath).Length > InstalledVariant.MaxCertificate + 64)
            {
                return null;
            }

            return Read(File.ReadAllBytes(signaturePath), File.ReadAllText(sumsPath), log);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log?.Invoke($"[IDENTITÉ] Signature de la variante installée illisible à côté de l'installeur : {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// La signature de l'installée et son empreinte, depuis la release de cette
    /// version sur GitHub, en HTTPS : la même confiance que pour l'installeur
    /// lui-même. <c>null</c> hors ligne.
    /// </summary>
    public static (VariantSignature Signature, string Sha256)? FindOnline(string version, Action<string>? log = null)
    {
        try
        {
            string release = $"https://github.com/{UpdateRules.Repository}/releases/download/v{version}/";
            byte[] signature = Http.GetByteArrayAsync(release + InstalledVariant.SignatureAsset).GetAwaiter().GetResult();
            string sums = Http.GetStringAsync(release + UpdateRules.ChecksumsAsset).GetAwaiter().GetResult();
            return Read(signature, sums, log);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            log?.Invoke($"[IDENTITÉ] Signature de la variante installée introuvable en ligne : {ex.Message}");
            return null;
        }
    }

    private static (VariantSignature Signature, string Sha256)? Read(byte[] signatureBytes, string sums, Action<string>? log)
    {
        if (InstalledVariant.Parse(signatureBytes) is not { } signature
            || UpdateRules.FindChecksum(sums, InstalledVariant.InstalledName) is not { } expected)
        {
            log?.Invoke("[IDENTITÉ] Signature de la variante installée illisible ou sans empreinte.");
            return null;
        }

        return (signature, expected);
    }

    private static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
