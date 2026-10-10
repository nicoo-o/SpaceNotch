using System.Buffers.Binary;

namespace SpaceNotch.Core.Setup;

/// <summary>Ce que fait l'installeur de l'exécutable qu'il vient de copier.</summary>
public enum VariantPlan
{
    /// <summary>Non signé : on rallume l'identité sur place, comme avant n° 51 ; aucune signature à casser.</summary>
    FlipInPlace,

    /// <summary>Signé, avec la signature de la variante installée : on la reconstruit octet pour octet.</summary>
    Rebuild,

    /// <summary>Signé, sans la signature de la variante installée : l'identité reste éteinte, la signature intacte.</summary>
    KeepOff
}

/// <summary>
/// La signature de la variante installée, telle que la CI l'extrait : la somme
/// de contrôle de l'en-tête PE, et la table des certificats (sa place et ses
/// octets). Avec elle, l'installeur refait le fichier signé par la CI.
/// </summary>
public sealed record VariantSignature(uint Checksum, uint CertificateOffset, byte[] Certificate);

/// <summary>Ce qui compte dans l'en-tête PE : où écrire la somme de contrôle et la table des certificats.</summary>
public readonly record struct PeHeader(int ChecksumOffset, int SecurityDirectoryOffset, uint CertificateOffset, uint CertificateSize)
{
    /// <summary>Vrai si l'exécutable porte une signature Authenticode.</summary>
    public bool HasSignature => CertificateSize > 0;
}

/// <summary>
/// Deux variantes du même exécutable (n° 51) : la « téléchargée » (installeur,
/// portable), identité éteinte, et l'« installée », identité allumée. La CI
/// construit et signe les deux. L'installeur n'a que la première : il la
/// copie, rallume l'identité (<see cref="IdentityManifest"/>), et remplace sa
/// propre signature par celle de la seconde, livrée à part (quelques Ko).
///
/// <para>
/// Pourquoi ça marche : la signature Authenticode couvre tout le fichier sauf
/// la somme de contrôle, l'entrée de la table des certificats et la table
/// elle-même, placée à la fin. Les deux variantes ne diffèrent ailleurs que
/// des 14 octets de l'identité. Le résultat est vérifié par son empreinte
/// SHA-256, publiée par la CI ; sinon, rien n'est gardé.
/// </para>
/// </summary>
public static class InstalledVariant
{
    /// <summary>La signature de la variante installée, actif de chaque release.</summary>
    public const string SignatureAsset = "SpaceNotch-identity.bin";

    /// <summary>Le nom de la variante installée dans SHA256SUMS.txt (le fichier lui-même n'est pas publié).</summary>
    public const string InstalledName = "SpaceNotch-installed.exe";

    /// <summary>Taille maximale admise pour la table des certificats.</summary>
    public const int MaxCertificate = 1024 * 1024;

    /// <summary>Octets à lire pour trouver l'en-tête PE.</summary>
    public const int HeaderBytes = 4096;

    private static ReadOnlySpan<byte> Magic => "SNSIG1\0\0"u8;

    private const int FixedLength = 8 + 4 + 4 + 4;

    /// <summary>Ce qu'il faut faire, selon que la copie est signée et que la signature de l'installée est là.</summary>
    public static VariantPlan Plan(bool sourceSigned, bool haveSignature) => !sourceSigned
        ? VariantPlan.FlipInPlace
        : haveSignature ? VariantPlan.Rebuild : VariantPlan.KeepOff;

    /// <summary>Le petit fichier publié : un en-tête fixe, puis la table des certificats.</summary>
    public static byte[] Serialize(VariantSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        var bytes = new byte[FixedLength + signature.Certificate.Length];
        Magic.CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), signature.Checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), signature.CertificateOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), (uint)signature.Certificate.Length);
        signature.Certificate.CopyTo(bytes, FixedLength);
        return bytes;
    }

    /// <summary>Relit le petit fichier ; <c>null</c> s'il n'est pas l'un des nôtres ou s'il est incohérent.</summary>
    public static VariantSignature? Parse(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < FixedLength || !bytes.StartsWith(Magic))
        {
            return null;
        }

        uint checksum = BinaryPrimitives.ReadUInt32LittleEndian(bytes[8..]);
        uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes[12..]);
        uint length = BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..]);

        // Une table vide n'a pas de place ; une table pleine commence alignée sur 8 octets.
        bool coherent = length == 0
            ? offset == 0
            : length <= MaxCertificate && offset > 0 && offset % 8 == 0;

        if (!coherent || bytes.Length != FixedLength + length)
        {
            return null;
        }

        return new VariantSignature(checksum, offset, bytes[FixedLength..].ToArray());
    }

    /// <summary>
    /// Lit l'en-tête PE dans les premiers octets du fichier ; <c>null</c> si ce
    /// n'est pas un exécutable PE32 ou PE32+ lisible.
    /// </summary>
    public static PeHeader? ReadHeader(ReadOnlySpan<byte> start)
    {
        if (start.Length < 0x40 || start[0] != (byte)'M' || start[1] != (byte)'Z')
        {
            return null;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(start[0x3C..]);

        // Signature « PE\0\0 », puis l'en-tête COFF (20 octets), puis l'en-tête optionnel.
        if (pe <= 0 || pe > start.Length - 24 || !start.Slice(pe, 4).SequenceEqual("PE\0\0"u8))
        {
            return null;
        }

        int optional = pe + 24;

        if (optional + 2 > start.Length)
        {
            return null;
        }

        ushort magic = BinaryPrimitives.ReadUInt16LittleEndian(start[optional..]);

        // Les répertoires de données commencent à 96 (PE32) ou 112 (PE32+) ; la table des certificats est le cinquième.
        int directories = magic switch
        {
            0x10B => optional + 96,
            0x20B => optional + 112,
            _ => -1
        };

        int security = directories + (4 * 8);

        if (directories < 0 || security + 8 > start.Length)
        {
            return null;
        }

        return new PeHeader(
            optional + 64,
            security,
            BinaryPrimitives.ReadUInt32LittleEndian(start[security..]),
            BinaryPrimitives.ReadUInt32LittleEndian(start[(security + 4)..]));
    }

    /// <summary>
    /// Longueur de l'image sans sa signature : là où commence la table des
    /// certificats, ou tout le fichier s'il n'est pas signé.
    /// </summary>
    public static long ImageLength(PeHeader header, long fileLength)
        => header.HasSignature && header.CertificateOffset + (long)header.CertificateSize == fileLength
            ? header.CertificateOffset
            : fileLength;

    /// <summary>
    /// Réécrit l'en-tête pour la variante installée : sa somme de contrôle, et
    /// l'entrée de sa table des certificats.
    /// </summary>
    public static void WriteHeader(Span<byte> start, PeHeader header, VariantSignature signature)
    {
        ArgumentNullException.ThrowIfNull(signature);
        BinaryPrimitives.WriteUInt32LittleEndian(start[header.ChecksumOffset..], signature.Checksum);
        BinaryPrimitives.WriteUInt32LittleEndian(start[header.SecurityDirectoryOffset..], signature.CertificateOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(start[(header.SecurityDirectoryOffset + 4)..], (uint)signature.Certificate.Length);
    }
}
